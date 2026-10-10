using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using UnityEngine;

namespace CMS21Together.Logic.Visuals;

// remote-visual-feedback D6: the tool in the remote avatar's hand is a ghost of the receiver's own ToolsManager tool,
// on the default layer (the tools render through CarToolsRenderCamera on their own layer), scaled to hand size.
// Part work has no prop: the game data holds no wrench mesh (docs/spikes/remote-visuals.md).
public class ToolProps
{
	public const float HandSize = 0.25f;
	private const int WorldLayer = 0;

	private readonly Transform hand;
	private readonly Dictionary<int, Ghost> cache = new Dictionary<int, Ghost>();
	private int shown = PlayerActivityState.NoTool;

	public ToolProps(Transform hand) => this.hand = hand;

	public bool Active => shown != PlayerActivityState.NoTool && cache.TryGetValue(shown, out var ghost) && ghost != null && ghost.IsAlive && ghost.Root.activeSelf;
	public string ToolName => Active ? ((ToolType)shown).ToString() : null;

	public void Show(PlayerActivityState activity)
	{
		bool canAtReservoir = activity != null && activity.Kind == ActivityKind.Fluid && !string.IsNullOrEmpty(activity.PartKey);
		int wanted = activity != null && !canAtReservoir && (activity.Kind == ActivityKind.Examine || activity.Kind == ActivityKind.Fluid) ? activity.ToolType : PlayerActivityState.NoTool;
		if (wanted == shown) return;
		if (shown != PlayerActivityState.NoTool && cache.TryGetValue(shown, out var old) && old != null && old.IsAlive) old.Root.SetActive(false);
		shown = wanted;
		if (wanted == PlayerActivityState.NoTool) return;
		if (!cache.TryGetValue(wanted, out var ghost) || ghost == null || !ghost.IsAlive)
		{
			using (VisualScope.Enter()) ghost = Build((ToolType)wanted);
			cache[wanted] = ghost;
		}
		if (ghost != null && ghost.IsAlive) ghost.Root.SetActive(true);
	}

	public void Destroy()
	{
		foreach (var ghost in cache.Values) ghost?.Destroy();
		cache.Clear();
		shown = PlayerActivityState.NoTool;
	}

	private Ghost Build(ToolType type)
	{
		var source = Source(type);
		if (source == null || hand == null) return null;
		var renderers = new List<Renderer>();
		foreach (var renderer in source.GetComponentsInChildren<MeshRenderer>(true)) renderers.Add(renderer);
		var ghost = Ghost.Create($"prop {type}", renderers, source.transform.position, source.transform.rotation, out string skip, WorldLayer);
		if (ghost == null)
		{
			VisualScope.Skip(VisualKind.Off, $"prop {skip}");
			return null;
		}
		float size = 0f;
		foreach (var renderer in ghost.Root.GetComponentsInChildren<MeshRenderer>(true))
		{
			var extents = renderer.bounds.size;
			size = Mathf.Max(size, Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)));
		}
		ghost.Root.transform.SetParent(hand, true);
		ghost.Root.transform.localPosition = Vector3.zero;
		float handScale = Mathf.Max(0.0001f, hand.lossyScale.x);
		ghost.SetScale((size > 0.001f ? Mathf.Clamp(HandSize / size, 0.05f, 4f) : 1f) / handScale);
		return ghost;
	}

	private static GameObject Source(ToolType type)
	{
		var tools = ToolsManager.Get();
		if (tools == null) return null;
		Component tool = type switch
		{
			ToolType.OBD => tools.ObdScanner,
			ToolType.Compression => tools.Compression,
			ToolType.Multimeter => tools.Multimeter,
			ToolType.TireTreadDepthTester => tools.TireTreadDepthTester,
			ToolType.CompoundMeter => tools.CompoundMeter,
			ToolType.FluidExtractor => tools.FluidExtractor,
			ToolType.OilBayonet => tools.OilBayonet,
			ToolType.OilRefill => tools.OilRefill,
			ToolType.BrakeRefill => tools.BrakeRefill,
			ToolType.CoolantRefill => tools.CoolantRefill,
			ToolType.WindscreenWashRefill => tools.WindscreenWashRefill,
			ToolType.PowerSteeringRefill => tools.PowerSteeringRefill,
			_ => null
		};
		if (tool != null) return tool.gameObject;
		return type switch
		{
			ToolType.OilDrain => tools.Oil_drain_h,
			ToolType.MountObjectSpray => tools.MountObjectSpray,
			_ => null
		};
	}
}
