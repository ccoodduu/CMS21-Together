using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CMS21Together.Logic.Driving;

public class RemoteColliderState
{
	public GameObject Object;
	public BoxCollider Box;
	public Rigidbody Body;
	public Vector3 Center;
	public Vector3 Size;
	public bool Enabled;
	public string Reason = "building";
	public bool Overlap;
	public bool Touching;
	public int Contacts;
	public float LastContactAt = -1f;
	public float PausedUntil;
	public int SeenSnaps;
	public int RaceHold;
	public int Renderers;
}

// track-collisions D2/D3: one kinematic box per observer copy on a layer of its own, moved to the copy's pose every
// physics step. It is on only while it does not overlap the local car, not within 1 s of a jump of either car, not
// in the copy that carries the local passenger and not from a race countdown until the cars are 10 m apart.
public static class RemoteCollider
{
	public const float SnapPauseSeconds = 1f;
	public const float RaceClearDistance = 10f;
	public const float JumpDistance = 5f;
	private const float ContactMargin = 0.05f;
	private const float OverlapMargin = 0.1f;
	private const float GroundClearance = 0.15f;
	private const float MaxLength = 7f;
	private const float MaxWidth = 3f;
	private const float MaxHeight = 3f;

	// Unnamed and without colliders on the tracks; 27 and 30 look free but are the game's "EMPTY3" and "StaticObjects".
	public const int Layer = 3;
	public static bool? Forced;

	private static Rigidbody localBody;
	private static int localMask;
	private static Vector3 localLast;
	private static float localPausedUntil;
	private static int matrixMask = -1;

	public static Rigidbody LocalBody => localBody;
	public static int LocalMask => localMask;

	public static void FixedUpdate()
	{
		if (!RemoteCars.All.Any()) return;
		RefreshLocal();
		foreach (var car in RemoteCars.All) Step(car);
	}

	public static void RefreshLocal()
	{
		var physics = TrackScenes.IsTrack(ClientScene.LocalScene) && GameMode.Get()?.GetCurrentMode() == gameMode.CarDrive ? PrepareCarPhysics.Get() : null;
		var body = physics != null && physics ? physics.rigidBody : null;
		if (body == null || !body)
		{
			localBody = null;
			return;
		}
		if (body != localBody)
		{
			localBody = body;
			localLast = body.position;
			localMask = 0;
			foreach (var collider in body.GetComponentsInChildren<Collider>(true))
				if (collider != null && !collider.isTrigger && collider.attachedRigidbody == body && collider.GetIl2CppType().Name != "WheelCollider")
					localMask |= 1 << collider.gameObject.layer;
			ApplyMatrix();
		}
		if ((body.position - localLast).sqrMagnitude > JumpDistance * JumpDistance) localPausedUntil = Time.time + SnapPauseSeconds;
		localLast = body.position;
	}

	private static void ApplyMatrix()
	{
		if (matrixMask == localMask) return;
		matrixMask = localMask;
		for (int layer = 0; layer < 32; layer++) Physics.IgnoreLayerCollision(Layer, layer, (localMask & (1 << layer)) == 0 || layer == Layer);
		Log.Info($"[Drive] Remote car colliders on layer {Layer} hit only layers {LayerList(localMask)} (the local car's body).");
	}

	private static string LayerList(int mask)
	{
		var names = new List<string>();
		for (int layer = 0; layer < 32; layer++)
			if ((mask & (1 << layer)) != 0) names.Add($"{layer} {LayerMask.LayerToName(layer)}");
		return string.Join(", ", names);
	}

	private static void Step(RemoteCar car)
	{
		if (!car.Ready || car.Stopped || car.ShownAfter < 0f || !car.Root.gameObject.activeInHierarchy) return;
		var state = car.Collider ??= Create(car);
		if (state == null) return;

		var rotation = car.Interpolator.Rotation;
		var center = car.Interpolator.Position + rotation * state.Center;
		bool jumped = (state.Body.position - center).sqrMagnitude >= JumpDistance * JumpDistance;
		if (car.Interpolator.Snaps != state.SeenSnaps || jumped)
		{
			state.SeenSnaps = car.Interpolator.Snaps;
			state.PausedUntil = Time.time + SnapPauseSeconds;
		}

		string reason = Reason(car, state, center, rotation);
		bool enable = reason == "on" || reason == "forced";
		if (enable && !state.Enabled) Log.Info($"[Drive] Collider of player {car.PlayerId}'s car on, {(localBody == null ? -1f : (center - localBody.position).magnitude):F1} m from the local car.");
		else if (!enable && state.Enabled) Log.Info($"[Drive] Collider of player {car.PlayerId}'s car off ({reason}).");

		if (enable && state.Enabled && !jumped)
		{
			state.Body.MovePosition(center);
			state.Body.MoveRotation(rotation);
		}
		else
		{
			state.Body.position = center;
			state.Body.rotation = rotation;
			state.Object.transform.SetPositionAndRotation(center, rotation);
		}
		state.Box.enabled = enable;
		state.Enabled = enable;
		state.Reason = reason;

		bool touching = enable && TouchesLocal(center, rotation, state.Size * 0.5f + Vector3.one * ContactMargin);
		if (touching && !state.Touching)
		{
			state.Contacts++;
			state.LastContactAt = Time.time;
		}
		state.Touching = touching;
	}

	private static string Reason(RemoteCar car, RemoteColliderState state, Vector3 center, Quaternion rotation)
	{
		if (Forced == true) return "forced";
		if (Forced == false) return "forced-off";
		if (!PlayerSettings.TrackCollisions) return "setting";
		if (ClientData.ServerInfo?.TrackCollisionsOff == true) return "host";
		if (RideAlong.IsPassenger) return "passenger";
		if (localBody == null) return "no-local-car";

		var race = TrackRaceSync.Current;
		if (race != null && race.Phase == TrackRaceSync.Phase.Countdown) state.RaceHold = race.RaceId;
		else if (state.RaceHold != 0 && (race == null || race.RaceId != state.RaceHold || (center - localBody.position).magnitude > RaceClearDistance)) state.RaceHold = 0;
		if (state.RaceHold != 0) return "race-start";

		if (Time.time < state.PausedUntil || Time.time < localPausedUntil) return "snap";
		var half = state.Size * 0.5f;
		state.Overlap = !state.Enabled && TouchesLocal(center, rotation, half + Vector3.one * OverlapMargin);
		return state.Overlap ? "overlap" : "on";
	}

	private static bool TouchesLocal(Vector3 center, Quaternion rotation, Vector3 halfExtents)
	{
		if (localBody == null || localMask == 0) return false;
		var hits = Physics.OverlapBox(center, halfExtents, rotation, localMask, QueryTriggerInteraction.Ignore);
		if (hits == null) return false;
		foreach (var hit in hits)
			if (hit != null && hit.attachedRigidbody == localBody) return true;
		return false;
	}

	private static RemoteColliderState Create(RemoteCar car)
	{
		var frame = car.Body != null && car.Body ? car.Body : car.Root;
		if (!Measure(car.Root, frame, out var bounds, out int renderers))
		{
			Log.Warn($"[Drive] Observer car of player {car.PlayerId} has no renderers to size its collider.");
			return null;
		}
		var size = bounds.size;
		var center = bounds.center;
		float clearance = Mathf.Min(GroundClearance, size.y * 0.25f);
		center.y += clearance * 0.5f;
		size.y -= clearance;
		size = new Vector3(Mathf.Min(size.x, MaxWidth), Mathf.Min(size.y, MaxHeight), Mathf.Min(size.z, MaxLength));

		var gameObject = new GameObject($"RemoteCollider[{car.PlayerId}]") { layer = Layer };
		var body = gameObject.AddComponent<Rigidbody>();
		body.isKinematic = true;
		body.useGravity = false;
		body.interpolation = RigidbodyInterpolation.None;
		body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
		var box = gameObject.AddComponent<BoxCollider>();
		box.size = size;
		box.enabled = false;
		Log.Info($"[Drive] Observer car of player {car.PlayerId}: collider box {size.x:F2} x {size.y:F2} x {size.z:F2} m from {renderers} renderers.");
		return new RemoteColliderState { Object = gameObject, Body = body, Box = box, Center = center, Size = size, SeenSnaps = car.Interpolator.Snaps, Renderers = renderers };
	}

	private static bool Measure(Transform root, Transform frame, out Bounds bounds, out int renderers)
	{
		bounds = default;
		renderers = 0;
		var inverse = Quaternion.Inverse(frame.rotation);
		var origin = frame.position;
		foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
		{
			if (renderer == null || !renderer.enabled) continue;
			Bounds local;
			var skinned = renderer.TryCast<SkinnedMeshRenderer>();
			if (skinned != null) local = skinned.localBounds;
			else
			{
				if (renderer.TryCast<MeshRenderer>() == null) continue;
				var filter = renderer.GetComponent<MeshFilter>();
				var mesh = filter == null ? null : filter.sharedMesh;
				if (mesh == null) continue;
				local = mesh.bounds;
			}
			var transform = renderer.transform;
			for (int corner = 0; corner < 8; corner++)
			{
				var point = local.center + Vector3.Scale(local.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
				var inFrame = inverse * (transform.TransformPoint(point) - origin);
				if (renderers == 0 && corner == 0) bounds = new Bounds(inFrame, Vector3.zero);
				else bounds.Encapsulate(inFrame);
			}
			renderers++;
		}
		return renderers > 0;
	}

	public static void Destroy(RemoteCar car)
	{
		if (car.Collider?.Object != null && car.Collider.Object) Object.Destroy(car.Collider.Object);
		car.Collider = null;
	}
}
