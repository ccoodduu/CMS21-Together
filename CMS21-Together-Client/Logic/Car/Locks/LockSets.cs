using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using UnityEngine;

namespace CMS21Together.Logic.Car.Locks;

public sealed class LockSet
{
	public int Loader;
	public CarLockKind Kind;
	public readonly List<string> X = new List<string>();
	public readonly List<string> S = new List<string>();
	public readonly List<long> Items = new List<long>();

	public IEnumerable<string> AllKeys => X.Concat(S);

	public override string ToString() => $"{Kind} X[{string.Join(",", X)}] S[{string.Join(",", S)}]{(Items.Count > 0 ? $" items[{string.Join(",", Items)}]" : "")}";
}

public sealed class CarRelations
{
	public int SpawnSeq;
	public PartRegistry Registry;
	public readonly Dictionary<string, List<string>> Ancestors = new Dictionary<string, List<string>>();
	public readonly Dictionary<string, HashSet<string>> Blocking = new Dictionary<string, HashSet<string>>();
	public readonly Dictionary<string, HashSet<string>> Fluids = new Dictionary<string, HashSet<string>>();
	public readonly Dictionary<string, string> MainOf = new Dictionary<string, string>();
	public readonly Dictionary<string, List<string>> Members = new Dictionary<string, List<string>>();
	public readonly Dictionary<string, List<string>> BodyConnected = new Dictionary<string, List<string>>();
	public readonly List<string> AllFluids = new List<string>();
	public readonly HashSet<string> EngineParts = new HashSet<string>();

	public IEnumerable<string> BlockingOf(string key) => Blocking.TryGetValue(key, out var set) ? set : Enumerable.Empty<string>();

	public IEnumerable<string> FluidsOf(string key) => Fluids.TryGetValue(key, out var set) ? set : Enumerable.Empty<string>();
}

public static class LockSets
{
	public const string OilKey = "f:EngineOil.0";
	private const string DrainPlugId = "korek_spustowy";

	private static readonly Dictionary<int, CarRelations> cache = new Dictionary<int, CarRelations>();

	public static string Scope => ClientData.ServerInfo?.LockScope == LockScope.Part ? LockScope.Part : LockScope.Connected;

	public static void Reset() => cache.Clear();

	public static void Forget(int loader) => cache.Remove(loader);

	public static string FluidKey(ModCarFluidType type, int id) => LockKeys.Fluid(type.ToString(), id);

	public static string FluidKey(CarFluidType type, int id) => type == CarFluidType.EngineOil ? OilKey : FluidKey((ModCarFluidType)(int)type, id);

	public static CarRelations Relations(int loader)
	{
		var sync = CarPartsSync.All.FirstOrDefault(s => s.Loader == loader);
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (sync?.Registry == null || carLoader == null) return null;
		if (cache.TryGetValue(loader, out var cached) && cached.SpawnSeq == sync.SpawnSeq && cached.Registry == sync.Registry) return cached;
		var relations = Build(carLoader, sync.Registry);
		relations.SpawnSeq = sync.SpawnSeq;
		cache[loader] = relations;
		return relations;
	}

	public static CarRelations Build(CarLoader carLoader, PartRegistry registry)
	{
		var relations = new CarRelations { Registry = registry };
		AddCarFluids(carLoader, relations);
		var keyOf = new Dictionary<int, string>();
		foreach (string key in registry.SubKeys) keyOf[registry.Sub(key).GetInstanceID()] = key;
		string KeyOf(PartScript script) => script != null && keyOf.TryGetValue(script.GetInstanceID(), out string key) ? key : null;

		foreach (string key in registry.SubKeys)
		{
			var script = registry.Sub(key);
			relations.Ancestors[key] = LockKeys.AncestorCandidates(key).Where(k => registry.Sub(k) != null).ToList();

			var main = script.unmountWithMainObject;
			string mainKey = main != null ? KeyOf(main) : null;
			relations.MainOf[key] = mainKey ?? key;
			var members = new List<string>();
			var unmountWith = script.unmountWith;
			for (int i = 0; unmountWith != null && i < unmountWith.Count; i++)
			{
				string member = KeyOf(unmountWith[i]);
				if (member != null && member != key) members.Add(member);
			}
			relations.Members[key] = members;

			var unblocks = script.unblockOnUnmount;
			for (int i = 0; unblocks != null && i < unblocks.Length; i++)
			{
				string other = KeyOf(unblocks[i]);
				if (other == null || other == key) continue;
				Link(relations.Blocking, key, other);
				Link(relations.Blocking, other, key);
			}

			foreach (string fluid in PartFluids(script, relations.AllFluids))
				Link(relations.Fluids, key, fluid);
		}

		var engine = carLoader.e_engine_h;
		if (engine != null)
		{
			foreach (string key in engine.GetComponentsInChildren<PartScript>(true).Select(KeyOf).Where(k => k != null)) relations.EngineParts.Add(key);
		}

		var body = carLoader.carParts;
		var bodyIndex = new Dictionary<string, int>();
		for (int i = 0; body != null && i < body.Count; i++)
			if (body[i]?.name != null && !bodyIndex.ContainsKey(body[i].name)) bodyIndex[body[i].name] = i;
		for (int i = 0; body != null && i < body.Count; i++)
		{
			var connected = new List<string>();
			var names = body[i].ConnectedParts;
			for (int n = 0; names != null && n < names.Count; n++)
				if (names[n] != null && bodyIndex.TryGetValue(names[n], out int other) && other != i) connected.Add(PartKeys.Body(other));
			relations.BodyConnected[PartKeys.Body(i)] = connected;
		}
		return relations;
	}

	private static void AddCarFluids(CarLoader carLoader, CarRelations relations)
	{
		var fluids = carLoader.FluidsData;
		relations.AllFluids.Add(OilKey);
		AddFluidList(relations.AllFluids, fluids.Brake, ModCarFluidType.Brake);
		AddFluidList(relations.AllFluids, fluids.EngineCoolant, ModCarFluidType.EngineCoolant);
		AddFluidList(relations.AllFluids, fluids.PowerSteering, ModCarFluidType.PowerSteering);
		AddFluidList(relations.AllFluids, fluids.WindscreenWash, ModCarFluidType.WindscreenWash);
	}

	private static void AddFluidList(List<string> keys, Il2CppSystem.Collections.Generic.List<FluidData> list, ModCarFluidType type)
	{
		for (int i = 0; list != null && i < list.Count; i++) keys.Add(FluidKey(type, i));
	}

	private static IEnumerable<string> PartFluids(PartScript script, List<string> allFluids)
	{
		var container = ContainerFluid(script);
		if (container != null) yield return container;

		switch ((int)script.FluidRefillLockType)
		{
			case 1:
				foreach (string key in allFluids) yield return key;
				break;
			case 2:
				yield return OilKey;
				break;
			case 3:
				foreach (string key in OfType(allFluids, ModCarFluidType.EngineCoolant)) yield return key;
				break;
			case 4:
				foreach (string key in OfType(allFluids, ModCarFluidType.Brake)) yield return key;
				break;
			case 5:
				foreach (string key in OfType(allFluids, ModCarFluidType.WindscreenWash)) yield return key;
				break;
			case 6:
				foreach (string key in OfType(allFluids, ModCarFluidType.PowerSteering)) yield return key;
				break;
		}

		int id = ParentFluidId(script);
		switch (script.sendMessageOnHide ?? "")
		{
			case "ZeroOil":
				yield return OilKey;
				break;
			case "ZeroAllEngineCoolant":
				foreach (string key in OfType(allFluids, ModCarFluidType.EngineCoolant)) yield return key;
				break;
			case "ZeroEngineCoolant":
				if (id >= 0) yield return FluidKey(ModCarFluidType.EngineCoolant, id);
				break;
			case "ZeroBrakeFluid":
				if (id >= 0) yield return FluidKey(ModCarFluidType.Brake, id);
				break;
			case "ZeroWindscreenWash":
				if (id >= 0) yield return FluidKey(ModCarFluidType.WindscreenWash, id);
				break;
			case "ZeroPowerSteering":
				if (id >= 0) yield return FluidKey(ModCarFluidType.PowerSteering, id);
				break;
		}

		if ((script.id ?? "").StartsWith(DrainPlugId)) yield return OilKey;
	}

	private static IEnumerable<string> OfType(List<string> allFluids, ModCarFluidType type)
	{
		string prefix = $"f:{type}.";
		return allFluids.Where(k => k.StartsWith(prefix));
	}

	private static CarFluid ParentFluid(PartScript script)
	{
		var parent = script.transform.parent;
		return parent == null ? null : parent.GetComponent<CarFluid>();
	}

	private static int ParentFluidId(PartScript script) => ParentFluid(script)?.ID ?? -1;

	private static string ContainerFluid(PartScript script)
	{
		var fluid = ParentFluid(script);
		if (fluid == null) return null;
		return (int)fluid.FluidType == (int)ModCarFluidType.EngineOil ? OilKey : FluidKey((ModCarFluidType)(int)fluid.FluidType, fluid.ID);
	}

	private static void Link(Dictionary<string, HashSet<string>> map, string from, string to)
	{
		if (!map.TryGetValue(from, out var set)) map[from] = set = new HashSet<string>();
		set.Add(to);
	}

	public static LockSet ForPart(int loader, string key, CarLockKind kind)
	{
		var relations = Relations(loader);
		if (relations == null || relations.Registry.Sub(key) == null) return null;
		string main = relations.MainOf.TryGetValue(key, out string m) ? m : key;
		var set = new LockSet { Loader = loader, Kind = kind };
		set.X.Add(main);
		foreach (string member in relations.Members.TryGetValue(main, out var members) ? members : new List<string>())
			if (!set.X.Contains(member)) set.X.Add(member);
		var shared = new List<string>();
		foreach (string part in set.X)
		{
			shared.AddRange(relations.Ancestors.TryGetValue(part, out var ancestors) ? ancestors : new List<string>());
			shared.AddRange(relations.BlockingOf(part));
			shared.AddRange(relations.FluidsOf(part));
			if (relations.EngineParts.Contains(part)) shared.Add(LockKeys.Engine);
		}
		Finish(set, shared);
		return set;
	}

	public static LockSet ForBody(int loader, int index)
	{
		var relations = Relations(loader);
		if (relations == null) return null;
		string key = PartKeys.Body(index);
		var set = new LockSet { Loader = loader, Kind = CarLockKind.BodyPart };
		set.X.Add(key);
		Finish(set, relations.BodyConnected.TryGetValue(key, out var connected) ? connected : new List<string>());
		return set;
	}

	public static LockSet ForFluid(int loader, CarLockKind kind, string fluidKey)
	{
		var set = new LockSet { Loader = loader, Kind = kind };
		set.X.Add(fluidKey);
		Finish(set, new List<string>());
		return set;
	}

	public static LockSet ForCrane(int loader)
	{
		var relations = Relations(loader);
		if (relations == null || relations.EngineParts.Count == 0) return null;
		var set = new LockSet { Loader = loader, Kind = CarLockKind.Crane };
		set.X.Add(LockKeys.Engine);
		Finish(set, new List<string>());
		return set;
	}

	public static LockSet ForTune(int loader, CarLoader carLoader)
	{
		var relations = Relations(loader);
		var set = new LockSet { Loader = loader, Kind = CarLockKind.Tune };
		set.X.Add(LockKeys.Tune);
		if (relations == null)
		{
			Finish(set, new List<string>());
			return set;
		}
		var parts = new List<PartScript>();
		var gearbox = carLoader.GetRoot()?.GetComponentInChildren<GearboxHandle>();
		if (gearbox != null) parts.Add(gearbox.gameObject.GetComponent<PartScript>());
		parts.AddRange(Details.CarDetailsIO.Modules(carLoader).Values.Select(m => m.PartScript));
		var shared = new List<string>();
		foreach (var part in parts)
		{
			if (part == null || !relations.Registry.TryGetSubPath(part, out var path)) continue;
			string key = PartKeys.Sub(path);
			if (set.X.Contains(key)) continue;
			set.X.Add(key);
			shared.AddRange(relations.Ancestors.TryGetValue(key, out var ancestors) ? ancestors : new List<string>());
			if (relations.EngineParts.Contains(key)) shared.Add(LockKeys.Engine);
		}
		Finish(set, shared);
		return set;
	}

	public static LockSet ForCar(int loader, CarLockKind kind)
	{
		var set = new LockSet { Loader = loader, Kind = kind };
		set.X.Add(LockKeys.Car);
		return set;
	}

	private static void Finish(LockSet set, IEnumerable<string> shared)
	{
		bool partOnly = Scope == LockScope.Part;
		foreach (string key in shared)
		{
			if (partOnly && key != LockKeys.Car) continue;
			if (!set.X.Contains(key) && !set.S.Contains(key)) set.S.Add(key);
		}
		if (!set.X.Contains(LockKeys.Car) && !set.S.Contains(LockKeys.Car)) set.S.Add(LockKeys.Car);
	}
}
