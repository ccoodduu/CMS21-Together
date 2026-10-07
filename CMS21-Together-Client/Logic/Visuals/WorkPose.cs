using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using UnityEngine;

namespace CMS21Together.Logic.Visuals;

// remote-visual-feedback D6: a procedural work pose on the remote avatar's Mixamo rig (no work clips exist). The
// yaw is re-applied every LateUpdate on top of PlayerInstance's network rotation; the arms after the pitch code.
public class WorkPose
{
	public const string Idle = "Idle";
	public const string Reach = "Reach";
	public const string Wrench = "Wrench";
	public const string ArmsUp = "ArmsUp";

	private const float YawSeconds = 0.3f;
	private const float StillSpeed = 0.2f;
	private const float BlendSeconds = 0.25f;
	private const float ElbowBend = 25f;
	private const float WrenchDegrees = 15f;
	private const float WrenchHz = 2f;
	private const float WrenchHoldSeconds = 1f;
	private const float CarCentreHeight = 0.8f;

	private readonly Transform root;
	private readonly Transform head;
	private readonly Transform rightUpper, rightFore, rightHand, leftUpper, leftFore;
	private readonly ToolProps props;
	private float yaw;
	private float weight;
	private byte lastProgress;
	private string lastKey;
	private float lastProgressChange = float.MinValue;
	private bool loggedMissing;

	public string Pose { get; private set; } = Idle;
	public float Facing { get; private set; }
	public bool HasArms => rightUpper != null && rightFore != null;
	public ToolProps Props => props;

	public WorkPose(Transform root, Transform head)
	{
		this.root = root;
		this.head = head;
		rightUpper = FindBone(root, "RightArm");
		rightFore = FindBone(root, "RightForeArm");
		rightHand = FindBone(root, "RightHand");
		leftUpper = FindBone(root, "LeftArm");
		leftFore = FindBone(root, "LeftForeArm");
		props = new ToolProps(rightHand != null ? rightHand : root);
	}

	public void ApplyYaw(PlayerActivityState activity, Vector3 velocity, float dt)
	{
		var target = Target(activity);
		float wanted = 0f;
		if (target.HasValue && new Vector2(velocity.x, velocity.z).magnitude < StillSpeed)
		{
			var to = target.Value - root.position;
			to.y = 0f;
			var forward = root.forward;
			forward.y = 0f;
			if (to.sqrMagnitude > 0.01f && forward.sqrMagnitude > 0.01f) wanted = Vector3.SignedAngle(forward, to, Vector3.up);
		}
		yaw = Mathf.MoveTowardsAngle(yaw, wanted, Mathf.Abs(wanted - yaw) / YawSeconds * dt + 1f);
		if (Mathf.Abs(yaw) > 0.01f) root.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * root.rotation;

		if (!target.HasValue)
		{
			Facing = 0f;
			return;
		}
		var flat = target.Value - root.position;
		flat.y = 0f;
		var facingForward = root.forward;
		facingForward.y = 0f;
		Facing = flat.sqrMagnitude > 0.01f ? Mathf.Abs(Vector3.Angle(facingForward, flat)) : 0f;
	}

	public void ApplyArms(PlayerActivityState activity, float dt)
	{
		var target = Target(activity);
		Pose = PoseFor(activity, target);
		props.Show(VisualScope.Enabled && activity != null ? activity : null);

		weight = Mathf.MoveTowards(weight, Pose == Idle ? 0f : 1f, dt / BlendSeconds);
		if (weight <= 0f || !target.HasValue) return;
		if (!HasArms)
		{
			if (!loggedMissing) Log.Warn($"[Visuals] {root.name}: right arm bones not found, the work pose turns the avatar only.");
			loggedMissing = true;
			return;
		}

		Aim(rightUpper, rightFore, target.Value);
		if (Pose == ArmsUp && leftUpper != null && leftFore != null) Aim(leftUpper, leftFore, target.Value);
		if (Pose == Wrench && rightHand != null)
			rightHand.Rotate(rightFore.up, Mathf.Sin(Time.time * WrenchHz * 2f * (float)System.Math.PI) * WrenchDegrees * weight, Space.World);
	}

	public void Dispose() => props.Destroy();

	private string PoseFor(PlayerActivityState activity, Vector3? target)
	{
		if (!VisualScope.Enabled || activity == null || activity.IsIdle || !target.HasValue) return Idle;
		if (activity.PartKey != lastKey || activity.Progress != lastProgress)
		{
			if (activity.PartKey == lastKey) lastProgressChange = Time.time;
			lastKey = activity.PartKey;
			lastProgress = activity.Progress;
		}
		if (head != null && target.Value.y > head.position.y + 0.1f) return ArmsUp;
		bool partWork = activity.Kind == ActivityKind.Unmount || activity.Kind == ActivityKind.Mount;
		return partWork && Time.time - lastProgressChange <= WrenchHoldSeconds ? Wrench : Reach;
	}

	private void Aim(Transform upper, Transform fore, Vector3 target)
	{
		var direction = target - upper.position;
		if (direction.sqrMagnitude < 0.0001f) return;
		var aimed = Quaternion.FromToRotation(upper.up, direction.normalized) * upper.rotation;
		upper.rotation = Quaternion.Slerp(upper.rotation, aimed, weight);
		fore.Rotate(fore.right, -ElbowBend * weight, Space.World);
	}

	public static Vector3? Target(PlayerActivityState activity)
	{
		if (activity == null || activity.IsIdle || !ClientScene.IsGarageReady) return null;
		switch (activity.Kind)
		{
			case ActivityKind.Machine:
				return MachinePosition((ModToolId)activity.ModTool);
			case ActivityKind.Unmount:
			case ActivityKind.Mount:
			case ActivityKind.BodyPanel:
				var part = PartPosition(activity.CarLoaderID, activity.PartKey);
				return part ?? CarCentre(activity.CarLoaderID);
			default:
				return CarCentre(activity.CarLoaderID);
		}
	}

	private static Vector3? PartPosition(int loader, string key)
	{
		if (loader < 0 || string.IsNullOrEmpty(key)) return null;
		var registry = CarPartsSync.Get(loader).Registry;
		if (registry == null) return null;
		if (key.StartsWith("b:")) return registry.Body(key)?.handle?.transform.position;
		var script = registry.Sub(key);
		return script == null ? null : script.transform.position;
	}

	private static Vector3? CarCentre(int loader)
	{
		if (loader < 0) return null;
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) return null;
		return carLoader.transform.position + Vector3.up * CarCentreHeight;
	}

	private static Vector3? MachinePosition(ModToolId tool)
	{
		var tools = ToolsManager.Get();
		if (tools == null) return null;
		Component logic = tool switch
		{
			ModToolId.TireChanger => tools.TireChangerLogic,
			ModToolId.WheelBalancer => tools.WheelBalancerLogic,
			ModToolId.SpringClamp => tools.SpringClampLogic,
			ModToolId.EngineStand1 => tools.EngineStandLogic,
			ModToolId.EngineStand2 => tools.EngineStandLogic,
			ModToolId.BrakeLathe => tools.BrakeLatheLogic,
			ModToolId.BatteryCharger => tools.BatteryChargerLogic,
			_ => null
		};
		return logic == null ? null : logic.transform.position + Vector3.up * CarCentreHeight;
	}

	private static Transform FindBone(Transform current, string name)
	{
		string currentName = current.name;
		if (currentName == name || currentName.EndsWith(":" + name)) return current;
		for (int i = 0; i < current.childCount; i++)
		{
			var found = FindBone(current.GetChild(i), name);
			if (found != null) return found;
		}
		return null;
	}
}
