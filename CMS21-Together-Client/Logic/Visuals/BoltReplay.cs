using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Visuals;

// remote-visual-feedback D4: while another player holds a part claim, ghost bolts replay MountObject.SetPosition for
// the actor's mean bolt progress. The real bolts are hidden meanwhile and show PartApplier's state again at the end.
public static class BoltReplay
{
	private static bool subscribed;

	public static IEnumerable<BoltEffect> Active => VisualScope.Effects.OfType<BoltEffect>().Where(e => !e.Ended);

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		PartClaims.ClaimChanged += OnClaimChanged;
		PartChanges.RemoteChangeApplying += OnApplying;
	}

	public static void OnActivity(int playerId, PlayerActivityState state)
	{
		if (state == null) return;
		foreach (var effect in Active.Where(e => e.PlayerId == playerId).ToList())
		{
			if (state.IsIdle || state.CarLoaderID != effect.Loader || state.PartKey != effect.Key) continue;
			effect.Report(state.ProgressFraction, state.Kind);
		}
	}

	private static void OnClaimChanged(int loader, IReadOnlyList<string> keys, int owner, bool fromSnapshot)
	{
		if (keys.Count == 0) return;
		if (owner == PartClaims.Released)
		{
			foreach (var effect in Active.Where(e => e.Loader == loader && keys.Contains(e.Key)).ToList()) effect.Released();
			return;
		}
		if (fromSnapshot || Client.Instance == null || owner == Client.Instance.ID) return;

		string key = keys[0];
		if (!key.StartsWith("s:") || Active.Any(e => e.Loader == loader && e.Key == key)) return;
		var script = VisualScope.RegistryOf(loader)?.Sub(key);
		if (script == null || script.MountObjects == null || script.MountObjects.Length == 0) return;
		if (VisualScope.Admit(VisualKind.Bolts, loader, script.transform.position) != null) return;

		using (VisualScope.Enter())
		{
			var effect = new BoltEffect(loader, key, owner, script, mount: script.IsUnmounted);
			if (effect.BoltCount == 0)
			{
				effect.DestroyAll();
				VisualScope.Skip(VisualKind.Bolts, "noRenderers");
				return;
			}
			VisualScope.Start(effect);
			effect.HideRealBolts();
		}
	}

	private static void OnApplying(int loader, List<CarBodyPartUpdatePacket> body, List<CarSubPartUpdatePacket> sub)
	{
		foreach (var record in sub)
		foreach (var effect in Active.Where(e => e.Loader == loader && e.Key == record.Key).ToList())
			effect.Commit();
	}
}

public class BoltEffect : VisualEffect
{
	public const float FinishSeconds = 0.2f;
	public const float UndoSeconds = 0.3f;
	public const float ReleaseGraceSeconds = 0.5f;
	private const float FollowSeconds = 0.15f;
	private const float Step16 = 1f / PlayerActivityState.ProgressSteps;

	private enum Mode
	{
		Follow,
		ReleasePending,
		Finish,
		Undo
	}

	private class Bolt
	{
		public MountObject Source;
		public Transform Parent;
		public Ghost Body;
		public Ghost Child;
		public Vector3 OldPos, PosUnMount, OldPosChild, ChildPosUnMount, Scale;
		public Quaternion OldRot, OldRotChild;
		public float Length;
		public bool Mirrored;
		public bool HasChild;
		public readonly List<Renderer> Renderers = new List<Renderer>();
	}

	private readonly List<Bolt> bolts = new List<Bolt>();
	private Mode mode = Mode.Follow;
	private bool mounting;
	private float reported, rate, reportedAt, shown, modeStart, modeFrom;

	public int BoltCount => bolts.Count;
	public int BoltsDone => bolts.Count == 0 ? 0 : Mathf.Clamp(Mathf.FloorToInt(shown * bolts.Count + 0.0001f), 0, bolts.Count);
	public float Shown => shown;
	public bool Mounting => mounting;

	public BoltEffect(int loader, string key, int owner, PartScript script, bool mount)
	{
		Bind(VisualKind.Bolts, loader, key, owner);
		mounting = mount;
		Phase = mount ? "mount" : "unmount";
		foreach (var mountObject in script.MountObjects)
		{
			if (mountObject == null || !mountObject) continue;
			var bolt = Capture(mountObject);
			if (bolt != null) bolts.Add(bolt);
		}
		Pose();
	}

	public override float HoldPoint => Elapsed;

	public void Report(float progress, ActivityKind kind)
	{
		if (mode != Mode.Follow) return;
		if (shown <= 0f && reported <= 0f && (kind == ActivityKind.Mount) != mounting)
		{
			mounting = kind == ActivityKind.Mount;
			Phase = mounting ? "mount" : "unmount";
		}
		if (progress > reported)
		{
			float since = Elapsed - reportedAt;
			rate = since > 0.01f ? (progress - reported) / since : 0f;
			reported = progress;
		}
		reportedAt = Elapsed;
	}

	public void Commit()
	{
		if (mode == Mode.Finish) return;
		SetMode(Mode.Finish, "finish");
	}

	public void Released()
	{
		if (mode == Mode.Follow) SetMode(Mode.ReleasePending, "released");
	}

	public void HideRealBolts()
	{
		foreach (var bolt in bolts)
		foreach (var renderer in bolt.Renderers)
			VisualScope.HideRenderer(this, renderer);
	}

	public void DestroyAll() => DestroyExtraGhosts();

	public override bool Step(float dt)
	{
		float since = Elapsed - modeStart;
		switch (mode)
		{
			case Mode.Follow:
				Follow(dt);
				break;
			case Mode.ReleasePending:
				Follow(dt);
				if (since >= ReleaseGraceSeconds) SetMode(Mode.Undo, "undo");
				break;
			case Mode.Finish:
				shown = Mathf.Lerp(modeFrom, 1f, Mathf.Clamp01(since / FinishSeconds));
				break;
			case Mode.Undo:
				shown = Mathf.Lerp(modeFrom, 0f, Mathf.Clamp01(since / UndoSeconds));
				break;
		}
		Pose();
		if (mode == Mode.Finish) return since < FinishSeconds;
		if (mode == Mode.Undo) return since < UndoSeconds;
		return true;
	}

	protected override void DestroyExtraGhosts()
	{
		foreach (var bolt in bolts)
		{
			bolt.Body?.Destroy();
			bolt.Child?.Destroy();
		}
		bolts.Clear();
	}

	private void SetMode(Mode next, string phase)
	{
		mode = next;
		modeStart = Elapsed;
		modeFrom = shown;
		Phase = phase;
	}

	private void Follow(float dt)
	{
		float target = Mathf.Min(1f, Mathf.Min(reported + rate * (Elapsed - reportedAt), reported + Step16));
		if (target > shown) shown += (target - shown) * (1f - Mathf.Exp(-dt / FollowSeconds));
	}

	private void Pose()
	{
		int n = bolts.Count;
		for (int i = 0; i < n; i++)
		{
			var bolt = bolts[i];
			if (bolt.Parent == null || !bolt.Parent) continue;
			float done = Mathf.Clamp01(shown * n - i);
			float mountState = mounting ? done : 1f - done;
			float t = 1f - mountState;
			float angle = (bolt.Mirrored ? mountState : t) * 360f * bolt.Length * 30f;
			var localRotation = bolt.OldRot * Quaternion.Euler(angle, 0f, 0f);
			var position = bolt.Parent.TransformPoint(Vector3.Lerp(bolt.OldPos, bolt.PosUnMount, t));
			var rotation = bolt.Parent.rotation * localRotation;
			bolt.Body?.SetPose(position, rotation);
			if (!bolt.HasChild || bolt.Child == null) continue;
			var childLocal = Vector3.Lerp(bolt.OldPosChild, bolt.ChildPosUnMount, t);
			bolt.Child.SetPose(position + rotation * Vector3.Scale(bolt.Scale, childLocal), rotation * bolt.OldRotChild);
			bolt.Child.SetScale(mountState >= 0.9f ? 1f : 0f);
		}
	}

	private static Bolt Capture(MountObject source)
	{
		var transform = source.transform;
		var bolt = new Bolt
		{
			Source = source,
			Parent = transform.parent,
			OldPos = source.oldPos,
			PosUnMount = source.posUnMount,
			OldRot = source.oldRot,
			Length = source.length,
			Mirrored = transform.localScale.x < 0f,
			Scale = transform.lossyScale,
			OldPosChild = source.oldPosChild,
			ChildPosUnMount = source.childPosUnMount,
			OldRotChild = source.oldRotChild,
		};
		if (bolt.Parent == null) return null;

		var child = source.child;
		var childRenderer = child == null ? null : child.GetComponent<Renderer>();
		var sources = source.renderers != null && source.renderers.Length > 0
			? source.renderers.ToArray().Where(r => r != null).ToList()
			: source.GetComponentsInChildren<Renderer>(true).ToList();
		var bodyRenderers = sources.Where(r => childRenderer == null || r.GetInstanceID() != childRenderer.GetInstanceID()).ToList();
		bolt.Renderers.AddRange(bodyRenderers);
		bolt.Body = Ghost.Create($"bolt {source.name}", bodyRenderers, transform.position, transform.rotation, out _);
		if (childRenderer != null)
		{
			bolt.HasChild = true;
			bolt.Renderers.Add(childRenderer);
			bolt.Child = Ghost.Create($"bolt child {source.name}", new List<Renderer> { childRenderer }, child.position, child.rotation, out _);
		}
		return bolt.Body == null && bolt.Child == null ? null : bolt;
	}
}
