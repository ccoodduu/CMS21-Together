using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Visuals;

// remote-visual-feedback D3: ghosts of parts moving off and on and of body panels swinging, started by row 1's
// RemoteChangeApplying/Applied. The authoritative apply runs between the two events, unchanged.
public static class PartGhosts
{
	public const float OffSeconds = 0.6f;
	public const float OnSeconds = 0.5f;
	public const float SwingSeconds = 0.6f;
	public const float PartDistance = 0.35f;
	public const float PanelDistance = 0.4f;
	private const float RecentOwnerSeconds = 2f;

	private static readonly Dictionary<string, (int Owner, float Time)> recentOwners = new Dictionary<string, (int, float)>();
	private static readonly Dictionary<string, PendingSwing> pendingSwings = new Dictionary<string, PendingSwing>();
	private static readonly HashSet<string> mounting = new HashSet<string>();
	private static bool subscribed;

	private class PendingSwing
	{
		public Ghost Ghost;
		public Vector3 Position;
		public Quaternion Rotation;
		public int Actor;
	}

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		PartChanges.RemoteChangeApplying += OnApplying;
		PartChanges.RemoteChangeApplied += OnApplied;
		PartClaims.ClaimChanged += OnClaimChanged;
	}

	public static void Reset()
	{
		recentOwners.Clear();
		foreach (var pending in pendingSwings.Values) pending.Ghost?.Destroy();
		pendingSwings.Clear();
	}

	public static int ActorOf(int loader, string key)
	{
		int owner = PartClaims.OwnerOf(loader, key);
		if (owner != PartClaims.Released) return owner;
		return recentOwners.TryGetValue(OwnerKey(loader, key), out var recent) && Time.time - recent.Time <= RecentOwnerSeconds ? recent.Owner : -1;
	}

	private static void OnClaimChanged(int loader, IReadOnlyList<string> keys, int owner, bool fromSnapshot)
	{
		foreach (string key in keys)
		{
			string id = OwnerKey(loader, key);
			if (owner != PartClaims.Released) recentOwners[id] = (owner, Time.time);
			else if (recentOwners.TryGetValue(id, out var recent)) recentOwners[id] = (recent.Owner, Time.time);
		}
	}

	private const PartFields MovingGroups = PartFields.Mount | PartFields.Switched | PartFields.All;

	private static bool Moves(PartFields changed) => changed == PartFields.None || (changed & MovingGroups) != 0;

	private static void OnApplying(int loader, List<CarBodyPartUpdatePacket> body, List<CarSubPartUpdatePacket> sub)
	{
		body = body.Where(r => Moves(r.Changed)).ToList();
		sub = sub.Where(r => Moves(r.Changed)).ToList();
		mounting.Clear();
		var sync = CarPartsSync.Get(loader);
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (sync.Registry == null || carLoader == null) return;
		VisualScope.CancelFor(loader, body.Select(r => r.Key).Concat(sub.Select(r => r.Key)), "newer change", keepBolts: true);

		using (VisualScope.Enter())
		{
			var offScripts = new Dictionary<string, PartScript>();
			foreach (var record in sub)
			{
				var script = sync.Registry.Sub(record.Key);
				if (script != null && record.Unmounted && !script.IsUnmounted) offScripts[record.Key] = script;
				else if (script != null && !record.Unmounted && script.IsUnmounted) mounting.Add(OwnerKey(loader, record.Key));
			}
			foreach (var pair in offScripts)
			{
				var leader = pair.Value.GetUnmountWithMainObject();
				if (leader != null && offScripts.Values.Any(s => s.GetInstanceID() == leader.GetInstanceID())) continue;
				var members = pair.Value.GetUnmountWith().ToArray().Where(m => m != null && offScripts.Values.Any(s => s.GetInstanceID() == m.GetInstanceID()));
				StartOff(loader, pair.Key, pair.Value, members.ToList(), carLoader);
			}

			foreach (var record in body)
			{
				var part = sync.Registry.Body(record.Key);
				if (part?.handle == null) continue;
				if (record.Unmounted && !part.Unmounted) StartPanel(loader, record.Key, part, carLoader, off: true);
				else if (!record.Unmounted && part.Unmounted) mounting.Add(OwnerKey(loader, record.Key));
				else if (!record.Unmounted && !part.Unmounted && record.Switched != part.Switched) PrepareSwing(loader, record.Key, part);
			}
		}
	}

	private static void OnApplied(int loader, List<CarBodyPartUpdatePacket> body, List<CarSubPartUpdatePacket> sub)
	{
		body = body.Where(r => Moves(r.Changed)).ToList();
		sub = sub.Where(r => Moves(r.Changed)).ToList();
		var sync = CarPartsSync.Get(loader);
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		using (VisualScope.Enter())
		{
			foreach (var record in body)
			{
				string id = OwnerKey(loader, record.Key);
				if (pendingSwings.TryGetValue(id, out var swing))
				{
					pendingSwings.Remove(id);
					var part = sync.Registry?.Body(record.Key);
					if (part?.handle == null || !swing.Ghost.IsAlive)
					{
						swing.Ghost.Destroy();
						continue;
					}
					var effect = new SwingEffect(loader, record.Key, swing.Actor, swing.Ghost, swing.Position, swing.Rotation, part.handle.transform);
					VisualScope.Start(effect);
					foreach (var renderer in VisibleRenderers(part.handle.transform)) VisualScope.HideRenderer(effect, renderer);
				}
			}
			if (sync.Registry == null || carLoader == null) return;
			foreach (var record in sub)
			{
				if (record.Unmounted) continue;
				var script = sync.Registry.Sub(record.Key);
				if (script == null || script.IsUnmounted || !mounting.Remove(OwnerKey(loader, record.Key))) continue;
				StartOn(loader, record.Key, script, carLoader);
			}
			foreach (var record in body)
			{
				var part = sync.Registry.Body(record.Key);
				if (part?.handle != null && !record.Unmounted && mounting.Remove(OwnerKey(loader, record.Key))) StartPanel(loader, record.Key, part, carLoader, off: false);
			}
		}
	}

	private static void StartOff(int loader, string key, PartScript script, List<PartScript> members, CarLoader carLoader)
	{
		var renderers = PartRenderers(script);
		foreach (var member in members) renderers.AddRange(PartRenderers(member));
		var position = script.transform.position;
		int actor = ActorOf(loader, key);
		if (!AdmitRemote(VisualKind.Off, loader, position, actor)) return;
		var ghost = Ghost.Create(key, renderers, position, script.transform.rotation, out string skip);
		if (ghost == null)
		{
			VisualScope.Skip(VisualKind.Off, skip);
			return;
		}
		var direction = UnmountDirection(script, carLoader);
		VisualScope.Start(new MoveEffect(VisualKind.Off, loader, key, actor, ghost, direction * PartDistance, OffSeconds,
			script.unmountSpinning ? script.transform.up : Vector3.zero, outward: true));
	}

	private static void StartOn(int loader, string key, PartScript script, CarLoader carLoader)
	{
		var renderers = PartRenderers(script);
		var position = script.transform.position;
		int actor = ActorOf(loader, key);
		if (!AdmitRemote(VisualKind.On, loader, position, actor)) return;
		var ghost = Ghost.Create(key, renderers, position, script.transform.rotation, out string skip);
		if (ghost == null)
		{
			VisualScope.Skip(VisualKind.On, skip);
			return;
		}
		var effect = new MoveEffect(VisualKind.On, loader, key, actor, ghost, UnmountDirection(script, carLoader) * PartDistance, OnSeconds,
			script.unmountSpinning ? script.transform.up : Vector3.zero, outward: false);
		VisualScope.Start(effect);
		foreach (var renderer in renderers) VisualScope.HideRenderer(effect, renderer);
	}

	private static void StartPanel(int loader, string key, CarPart part, CarLoader carLoader, bool off)
	{
		var kind = VisualKind.Panel;
		var handle = part.handle.transform;
		int actor = ActorOf(loader, key);
		if (!AdmitRemote(kind, loader, handle.position, actor)) return;
		var renderers = VisibleRenderers(handle);
		var ghost = Ghost.Create(key, renderers, handle.position, handle.rotation, out string skip);
		if (ghost == null)
		{
			VisualScope.Skip(kind, skip);
			return;
		}
		var away = handle.position - carLoader.transform.position;
		away.y = 0f;
		if (away.sqrMagnitude < 0.0001f) away = carLoader.transform.forward;
		var effect = new MoveEffect(kind, loader, key, actor, ghost, away.normalized * PanelDistance, off ? OffSeconds : OnSeconds, Vector3.zero, outward: off);
		VisualScope.Start(effect);
		if (!off) foreach (var renderer in renderers) VisualScope.HideRenderer(effect, renderer);
	}

	private static void PrepareSwing(int loader, string key, CarPart part)
	{
		var handle = part.handle.transform;
		int actor = ActorOf(loader, key);
		if (!AdmitRemote(VisualKind.Swing, loader, handle.position, actor)) return;
		var ghost = Ghost.Create(key, VisibleRenderers(handle), handle.position, handle.rotation, out string skip);
		if (ghost == null)
		{
			VisualScope.Skip(VisualKind.Swing, skip);
			return;
		}
		pendingSwings[OwnerKey(loader, key)] = new PendingSwing { Ghost = ghost, Position = handle.position, Rotation = handle.rotation, Actor = actor };
	}

	private static bool AdmitRemote(VisualKind kind, int loader, Vector3 position, int actor)
	{
		if (Client.Instance != null && actor == Client.Instance.ID)
		{
			VisualScope.Skip(kind, "own");
			return false;
		}
		return VisualScope.Admit(kind, loader, position) == null;
	}

	private static Vector3 UnmountDirection(PartScript script, CarLoader carLoader)
	{
		var direction = script.GetUnmountDir();
		if (direction.sqrMagnitude > 0.0001f) return direction.normalized;
		var away = script.transform.position - carLoader.transform.position;
		away.y = 0f;
		return away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.up;
	}

	// The part's own renderers: not its bolts (BoltReplay draws those), not child parts, not the enableOnUnmount
	// stand-ins. Renderer.enabled is ignored: it is off for culled parts (spike 1.3), and PartApplier turns
	// some renderers on only 0.5 s after a mount.
	public static List<Renderer> PartRenderers(PartScript script)
	{
		var excluded = new HashSet<int>();
		if (script.MountObjects != null)
			foreach (var mountObject in script.MountObjects)
				if (mountObject != null)
					foreach (var renderer in mountObject.GetComponentsInChildren<Renderer>(true)) excluded.Add(renderer.GetInstanceID());
		foreach (var child in script.GetComponentsInChildren<PartScript>(true))
			if (child.GetInstanceID() != script.GetInstanceID())
				foreach (var renderer in child.GetComponentsInChildren<Renderer>(true)) excluded.Add(renderer.GetInstanceID());
		if (script.enableOnUnmount != null)
			foreach (var go in script.enableOnUnmount)
				if (go != null)
					foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) excluded.Add(renderer.GetInstanceID());

		var result = new List<Renderer>();
		foreach (var renderer in script.GetComponentsInChildren<MeshRenderer>(false))
			if (!excluded.Contains(renderer.GetInstanceID())) result.Add(renderer);
		return result;
	}

	internal static List<Renderer> VisibleRenderers(Transform root)
	{
		var result = new List<Renderer>();
		foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(false))
			if (renderer.enabled) result.Add(renderer);
		return result;
	}

	private static string OwnerKey(int loader, string key) => $"{loader}/{key}";
}

// Off: from the part's pose outward and fading out. On: from outward to the part's pose, fading in, with the real
// renderers hidden until it lands.
public class MoveEffect : VisualEffect
{
	private readonly Vector3 start;
	private readonly Quaternion rotation;
	private readonly Vector3 offset;
	private readonly Vector3 spinAxis;
	private readonly float duration;
	private readonly bool outward;

	public MoveEffect(VisualKind kind, int loader, string key, int playerId, Ghost ghost, Vector3 offset, float duration, Vector3 spinAxis, bool outward)
	{
		Bind(kind, loader, key, playerId);
		this.ghost = ghost;
		start = ghost.Root.transform.position;
		rotation = ghost.Root.transform.rotation;
		this.offset = offset;
		this.duration = duration;
		this.spinAxis = spinAxis;
		this.outward = outward;
		Phase = outward ? "out" : "in";
		Apply(0f);
	}

	public Vector3 Origin => start;

	public Vector3 Offset => offset;

	public override float HoldPoint => duration * 0.5f;

	public override bool Step(float dt)
	{
		float t = Mathf.Clamp01(Elapsed / duration);
		Apply(t);
		return t < 1f;
	}

	private void Apply(float t)
	{
		float away = outward ? EaseOut(t) : 1f - EaseOut(t);
		var spin = spinAxis == Vector3.zero ? Quaternion.identity : Quaternion.AngleAxis(360f * away, spinAxis);
		ghost.SetPose(start + offset * away, spin * rotation);
		ghost.SetAlpha(outward ? 1f - t : t);
	}
}

// Rigid swing from the panel's pose before the instant switch to its pose after it, about the hinge axis that the
// two poses imply.
public class SwingEffect : VisualEffect
{
	private readonly Vector3 fromPosition;
	private readonly Quaternion fromRotation;
	private readonly Vector3 toPosition;
	private readonly Quaternion toRotation;
	private readonly Vector3 axis;
	private readonly float angle;
	private readonly Vector3 hinge;
	private readonly bool rigid;

	public SwingEffect(int loader, string key, int playerId, Ghost ghost, Vector3 fromPosition, Quaternion fromRotation, Transform target)
	{
		Bind(VisualKind.Swing, loader, key, playerId);
		this.ghost = ghost;
		this.fromPosition = fromPosition;
		this.fromRotation = fromRotation;
		toPosition = target.position;
		toRotation = target.rotation;
		(toRotation * Quaternion.Inverse(fromRotation)).ToAngleAxis(out angle, out axis);
		if (angle > 180f) angle -= 360f;
		rigid = Mathf.Abs(angle) > 1f && axis.sqrMagnitude > 0.5f;
		if (rigid) hinge = fromPosition - HingeOffset(toPosition - fromPosition, axis.normalized, angle * (float)(System.Math.PI / 180.0));
		Phase = "swing";
	}

	public override float HoldPoint => PartGhosts.SwingSeconds * 0.5f;

	public override bool Step(float dt)
	{
		float t = EaseInOut(Mathf.Clamp01(Elapsed / PartGhosts.SwingSeconds));
		if (rigid)
		{
			var turn = Quaternion.AngleAxis(angle * t, axis);
			ghost.SetPose(hinge + turn * (fromPosition - hinge), turn * fromRotation);
		}
		else
		{
			ghost.SetPose(Vector3.Lerp(fromPosition, toPosition, t), Quaternion.Slerp(fromRotation, toRotation, t));
		}
		return Elapsed < PartGhosts.SwingSeconds;
	}

	// For a rotation by theta about unit axis a through hinge h: to - from = (R - I)(from - h). In the plane normal to
	// a, R - I is multiplication by e^(i theta) - 1, so from - h = d / (e^(i theta) - 1).
	private static Vector3 HingeOffset(Vector3 delta, Vector3 a, float theta)
	{
		var d = delta - Vector3.Dot(delta, a) * a;
		float c = Mathf.Cos(theta) - 1f, s = Mathf.Sin(theta);
		float norm = c * c + s * s;
		return norm < 1e-6f ? Vector3.zero : (c * d - s * Vector3.Cross(a, d)) / norm;
	}
}
