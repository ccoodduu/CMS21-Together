using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Player;
using CMS21Together.Logic.Tools;
using CMS21Together.Logic.Tools.CarTools;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Visuals;

// remote-visual-feedback D5: polls what the local player does at 4 Hz and sends it only on change, at most four
// times a second (a change inside the window waits for the next slot and is coalesced with later ones).
public static class ActivityCapture
{
	public const float PollSeconds = 0.25f;
	public const int MaxPerSecond = 4;
	private const int TraceLimit = 200;

	private static readonly HashSet<int> FluidTools = new HashSet<int>
	{
		(int)ToolType.OilRefill, (int)ToolType.OilDrain, (int)ToolType.BrakeRefill, (int)ToolType.CoolantRefill,
		(int)ToolType.WindscreenWashRefill, (int)ToolType.PowerSteeringRefill, (int)ToolType.MountObjectSpray, (int)ToolType.FluidExtractor
	};

	private static readonly HashSet<int> ExamineTools = new HashSet<int>
	{
		(int)ToolType.OBD, (int)ToolType.Compression, (int)ToolType.Multimeter, (int)ToolType.TireTreadDepthTester,
		(int)ToolType.CompoundMeter, (int)ToolType.OilBayonet
	};

	private static readonly Dictionary<int, string> leaderKeys = new Dictionary<int, string>();
	private static readonly List<string> trace = new List<string>();
	private static PlayerActivityState sent = PlayerActivityState.Idle();
	private static PlayerActivityState pending;
	private static float nextPoll;
	private static float lastSend = float.MinValue;
	private static float windowStart;
	private static int windowCount;
	private static bool subscribed;

	public static PlayerActivityState Current => sent;
	public static int Sent { get; private set; }
	public static int Dropped { get; private set; }
	public static bool Tracing { get; set; }
	public static IReadOnlyList<string> Trace => trace;

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		PartClaims.ClaimChanged += OnClaimChanged;
		ClientScene.LeavingScene += (from, to) => Publish(PlayerActivityState.Idle(), force: true);
	}

	public static void Reset()
	{
		leaderKeys.Clear();
		sent = PlayerActivityState.Idle();
		pending = null;
		lastSend = float.MinValue;
		windowCount = 0;
		Sent = 0;
		Dropped = 0;
		trace.Clear();
	}

	public static void Update()
	{
		float now = Time.realtimeSinceStartup;
		if (now >= nextPoll)
		{
			nextPoll = now + PollSeconds;
			var current = Capture();
			if (!current.SameAs(pending ?? sent)) pending = current.SameAs(sent) ? null : current;
		}
		if (pending != null && now - lastSend >= 1f / MaxPerSecond)
		{
			var next = pending;
			pending = null;
			Publish(next, force: false);
		}
	}

	public static PlayerActivityState Capture()
	{
		var idle = PlayerActivityState.Idle();
		if (Client.Instance == null || !Client.Instance.IsConnectionValid || !ClientScene.IsGarageReady || !ClientData.IsInitialSyncFinished) return idle;
		if (SeatEngine.IsSeated || SeatEngine.InSeatedMode) return idle;

		return FromClaim() ?? FromCarTool() ?? FromMachine() ?? FromHandTool() ?? FromInterior() ?? idle;
	}

	public static float PartProgress(PartScript script, bool mounting)
	{
		if (script?.MountObjects == null || script.MountObjects.Length == 0) return 0f;
		float sum = 0f;
		int count = 0;
		foreach (var mountObject in script.MountObjects)
		{
			if (mountObject == null) continue;
			float state = Mathf.Clamp01(mountObject.GetMountState());
			sum += mounting ? state : 1f - state;
			count++;
		}
		return count == 0 ? 0f : sum / count;
	}

	private static void OnClaimChanged(int loader, IReadOnlyList<string> keys, int owner, bool fromSnapshot)
	{
		if (Client.Instance == null || keys.Count == 0) return;
		if (owner == Client.Instance.ID) leaderKeys[loader] = keys[0];
		else if (leaderKeys.TryGetValue(loader, out string key) && keys.Contains(key)) leaderKeys.Remove(loader);
	}

	private static PlayerActivityState FromClaim()
	{
		int me = Client.Instance.ID;
		foreach (var sync in CarPartsSync.All)
		{
			var held = PartClaims.Held(sync.Loader).Where(c => c.Value == me).Select(c => c.Key).ToList();
			if (held.Count == 0) continue;
			string key = leaderKeys.TryGetValue(sync.Loader, out string leader) && held.Contains(leader) ? leader : held.OrderBy(k => k, System.StringComparer.Ordinal).First();
			var state = new PlayerActivityState { CarLoaderID = sync.Loader, PartKey = key };
			if (key.StartsWith("b:"))
			{
				state.Kind = ActivityKind.BodyPanel;
				return state;
			}
			var script = sync.Registry?.Sub(key);
			var mode = GameMode.Get()?.currentMode ?? gameMode.Garage;
			bool mounting = mode == gameMode.PartMount || mode == gameMode.GroupMount
			                || (mode != gameMode.PartUnMount && mode != gameMode.GroupUnMount && script != null && script.IsUnmounted);
			state.Kind = mounting ? ActivityKind.Mount : ActivityKind.Unmount;
			state.Progress = PlayerActivityState.QuantizeProgress(PartProgress(script, mounting));
			return state;
		}
		return null;
	}

	private static PlayerActivityState FromCarTool()
	{
		foreach (var work in CarToolActions.LocalWork)
			return new PlayerActivityState { Kind = ActivityKind.CarTool, CarLoaderID = work.Loader, ModTool = (int)work.Tool };
		return null;
	}

	private static PlayerActivityState FromMachine()
	{
		foreach (var tool in ToolSync.OwnClaims)
			return new PlayerActivityState { Kind = ActivityKind.Machine, ModTool = (int)tool };
		return null;
	}

	private static PlayerActivityState FromHandTool()
	{
		var tools = ToolsManager.Get();
		if (tools == null || !tools.ToolIsActive) return null;
		int type = (int)tools.currentUsedTool;
		ActivityKind kind;
		if (ExamineTools.Contains(type)) kind = ActivityKind.Examine;
		else if (FluidTools.Contains(type)) kind = ActivityKind.Fluid;
		else return null;
		var state = new PlayerActivityState { Kind = kind, ToolType = type, CarLoaderID = MouseOverLoader() };
		if (kind == ActivityKind.Fluid) AddPour(tools, state);
		return state;
	}

	private static void AddPour(ToolsManager tools, PlayerActivityState state)
	{
		var logic = FluidReplay.Refill(tools, (ToolType)state.ToolType)?.fluidRefillLogic;
		if (logic == null || !logic.gameObject.activeSelf || state.CarLoaderID < 0 || tools.ItemWorkOn == null) return;
		var cap = tools.ItemWorkOn.GetComponent<PartScript>();
		var registry = VisualScope.RegistryOf(state.CarLoaderID);
		if (cap == null || registry == null || !registry.TryGetSubPath(cap, out int[] path)) return;
		state.PartKey = PartKeys.Sub(path);
		state.Progress = PlayerActivityState.QuantizeProgress(logic.power);
	}

	private static PlayerActivityState FromInterior()
	{
		var mode = GameMode.Get()?.currentMode;
		if (mode != gameMode.Interior && mode != gameMode.InteriorAssemble && mode != gameMode.InteriorDisassemble) return null;
		return new PlayerActivityState { Kind = ActivityKind.Interior, CarLoaderID = MouseOverLoader() };
	}

	private static int MouseOverLoader()
	{
		var carLoader = GameScript.Get()?.GetIOMouseOverCarLoader2();
		var places = CarLoaderPlaces.Get();
		if (carLoader == null || places == null) return PlayerActivityState.NoCar;
		int loader = places.GetCarLoaderId(carLoader);
		return loader < 0 ? PlayerActivityState.NoCar : loader;
	}

	private static void Publish(PlayerActivityState state, bool force)
	{
		if (Client.Instance == null || !Client.Instance.IsConnectionValid) return;
		if (!force && state.SameAs(sent)) return;
		float now = Time.realtimeSinceStartup;
		if (now - windowStart >= 1f)
		{
			windowStart = now;
			windowCount = 0;
		}
		if (++windowCount > MaxPerSecond)
		{
			Dropped++;
			Note($"dropped {state}");
			return;
		}
		sent = state;
		lastSend = now;
		Sent++;
		Client.Instance.Send(new PlayerActivityPacket { PlayerId = Client.Instance.ID, State = state });
		Note($"sent {state}");
	}

	private static void Note(string text)
	{
		if (!Tracing) return;
		string line = $"{Time.realtimeSinceStartup:F2} {text}";
		trace.Add(line);
		if (trace.Count > TraceLimit) trace.RemoveAt(0);
		Log.Info($"[VisualTrace] {line}");
	}
}
