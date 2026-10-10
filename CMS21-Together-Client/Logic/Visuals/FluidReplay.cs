using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Locks;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Player;
using UnityEngine;

namespace CMS21Together.Logic.Visuals;

// remote-fluid-visuals D2: starts a drain or pour replay once per received activity value of another player; the
// effect itself follows that player's activity and ends when it no longer asks for it.
public static class FluidReplay
{
	public const string CopyPrefix = "TogetherFluid";

	private static readonly Dictionary<int, PlayerActivityState> tried = new Dictionary<int, PlayerActivityState>();

	public static IEnumerable<FluidEffect> Active => VisualScope.Effects.OfType<FluidEffect>().Where(e => !e.Ended);

	public static void Reset() => tried.Clear();

	public static bool WantsDrain(PlayerActivityState activity, int loader) =>
		activity != null && activity.Kind == ActivityKind.CarTool && activity.ModTool == (int)ModToolId.OilBin
		&& activity.CarLoaderID >= 0 && (loader < 0 || activity.CarLoaderID == loader);

	public static bool WantsPour(PlayerActivityState activity, int loader, int toolType, string partKey) =>
		activity != null && activity.Kind == ActivityKind.Fluid && IsRefill(activity.ToolType) && !string.IsNullOrEmpty(activity.PartKey)
		&& activity.CarLoaderID >= 0 && (loader < 0 || (activity.CarLoaderID == loader && activity.ToolType == toolType && activity.PartKey == partKey));

	public static bool IsRefill(int toolType) =>
		toolType == (int)ToolType.OilRefill || toolType == (int)ToolType.BrakeRefill || toolType == (int)ToolType.CoolantRefill
		|| toolType == (int)ToolType.WindscreenWashRefill || toolType == (int)ToolType.PowerSteeringRefill;

	public static FluidRefill Refill(ToolsManager tools, ToolType type) => tools == null ? null : type switch
	{
		ToolType.OilRefill => tools.OilRefill,
		ToolType.BrakeRefill => tools.BrakeRefill,
		ToolType.CoolantRefill => tools.CoolantRefill,
		ToolType.WindscreenWashRefill => tools.WindscreenWashRefill,
		ToolType.PowerSteeringRefill => tools.PowerSteeringRefill,
		_ => null
	};

	public static void Update()
	{
		if (!ClientScene.IsGarageReady || !ClientData.IsInitialSyncFinished || SyncTracker.InSnapshot) return;
		foreach (var player in PresenceManager.Roster.Values)
		{
			var record = player.Record;
			if (record == null) continue;
			var activity = record.Activity;
			if (tried.TryGetValue(record.PlayerId, out var last) && ReferenceEquals(last, activity)) continue;
			tried[record.PlayerId] = activity;
			if (WantsDrain(activity, -1)) TryDrain(record.PlayerId, activity.CarLoaderID);
			else if (WantsPour(activity, -1, -1, null)) TryPour(record.PlayerId, activity);
		}
	}

	private static void TryDrain(int playerId, int loader)
	{
		if (Active.Any(e => e.PlayerId == playerId && e.Kind == VisualKind.Drain && e.Loader == loader)) return;
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || !carLoader.IsCarLoaded() || !CarPartsSync.IsReady(loader)) return;
		var plug = DrainEffect.FindPlug(carLoader);
		if (plug == null)
		{
			VisualScope.Skip(VisualKind.Drain, "noPlug");
			return;
		}
		if (VisualScope.Admit(VisualKind.Drain, loader, plug.position) != null) return;
		var source = ToolsManager.Get()?.Oil_drain_h;
		if (source == null)
		{
			VisualScope.Skip(VisualKind.Drain, "noTool");
			return;
		}

		Launch(new DrainEffect(loader, LockSets.OilKey, playerId, carLoader, plug), e => e.Build(source))?.HidePlug();
	}

	private static void TryPour(int playerId, PlayerActivityState activity)
	{
		int loader = activity.CarLoaderID;
		if (Active.Any(e => e.PlayerId == playerId && e.Kind == VisualKind.Pour)) return;
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || !carLoader.IsCarLoaded() || !CarPartsSync.IsReady(loader)) return;
		var cap = VisualScope.RegistryOf(loader)?.Sub(activity.PartKey);
		var refill = Refill(ToolsManager.Get(), (ToolType)activity.ToolType);
		if (cap == null || refill == null || refill.fluidRefillLogic == null)
		{
			VisualScope.Skip(VisualKind.Pour, cap == null ? "noPart" : "noTool");
			return;
		}
		if (VisualScope.Admit(VisualKind.Pour, loader, cap.transform.position) != null) return;

		Launch(new PourEffect(loader, playerId, activity, carLoader, cap), e => e.Build(refill));
	}

	private static T Launch<T>(T effect, System.Func<T, bool> build) where T : FluidEffect
	{
		using (VisualScope.Enter())
		{
			string reason = "noParticles";
			try
			{
				if (build(effect))
				{
					VisualScope.Start(effect);
					return effect;
				}
			}
			catch (System.Exception e)
			{
				reason = "failed";
				Log.Error($"[Visuals] {effect.Kind} on loader {effect.Loader} for player {effect.PlayerId} failed to start: {e}");
			}
			effect.DestroyCopy();
			VisualScope.Skip(effect.Kind, reason);
			return null;
		}
	}

	// Under an inactive holder no Awake runs, so the copied scripts are removed before they ever start. Colliders go
	// too: the copy must not catch the receiver's clicks.
	public static GameObject Copy(GameObject source, string name)
	{
		var holder = new GameObject($"{CopyPrefix}Staging");
		holder.SetActive(false);
		var copy = Object.Instantiate(source, holder.transform);
		foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
			if (behaviour != null) Object.DestroyImmediate(behaviour);
		foreach (var collider in copy.GetComponentsInChildren<Collider>(true))
			if (collider != null) Object.DestroyImmediate(collider);
		foreach (var camera in copy.GetComponentsInChildren<Camera>(true))
			if (camera != null) Object.DestroyImmediate(camera);
		copy.name = $"{CopyPrefix}[{name}]";
		copy.transform.SetParent(null, false);
		copy.transform.localScale = source.transform.lossyScale;
		Object.Destroy(holder);
		copy.SetActive(true);
		return copy;
	}

	public static string PathFrom(Transform root, Transform target)
	{
		if (root == null || target == null || !target.IsChildOf(root)) return null;
		var names = new List<string>();
		for (var t = target; t != root; t = t.parent) names.Add(t.name);
		names.Reverse();
		return string.Join("/", names);
	}

	public static Transform Find(GameObject copy, string path) =>
		path == null ? null : path.Length == 0 ? copy.transform : copy.transform.Find(path);
}
