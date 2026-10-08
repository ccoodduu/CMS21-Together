using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Digest;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Reconciliation;
using Newtonsoft.Json;

namespace CMS21_Together_Server.Data.Cars
{
	// --check-merges: the D1 part record rules of PartRecordMerge and the D5 details merge, in-process and without clients.
	public static class MergeCheck
	{
		private static int failures;

		public static int Run()
		{
			failures = 0;
			CheckParts();
			CheckDetails();
			CheckDigests();
			Console.WriteLine($"merges check: {(failures == 0 ? "OK" : $"FAILED ({failures})")}");
			return failures == 0 ? 0 : 1;
		}

		private static void CheckParts()
		{
			var stored = Sub(unmounted: false, condition: 0.5f, examined: false);

			var mount = Sub(unmounted: false, id: "disc_new", condition: 0.9f, quality: 3, changed: PartFields.Mount | PartFields.Identity | PartFields.Condition);
			var asMounted = Sub(unmounted: true, examined: true);
			var whole = PartRecordMerge.Normalise(asMounted, mount, Pre(true));
			Check("a mount record is taken whole", whole.Outcome == MergeOutcome.Whole && whole.Written == PartFields.All && whole.Record.PartId == "disc_new"
				&& Math.Abs(whole.Record.Condition - 0.9f) < 0.001f && whole.Record.Quality == 3 && !whole.Record.Unmounted);
			Check("a mount record keeps the stored IsExamined", whole.Record.IsExamined);

			var unmount = Sub(unmounted: true, condition: 0.7f, bolts: 0.2f, changed: PartFields.Mount | PartFields.Bolts);
			var off = PartRecordMerge.Normalise(stored, unmount, Pre(false));
			Check("an unmount copies its masked groups", off.Outcome == MergeOutcome.Merged && off.Record.Unmounted && Math.Abs(off.Record.MountObjectData.Condition[0] - 0.2f) < 0.001f);
			Check("an unmount keeps the unmasked groups", Math.Abs(off.Record.Condition - 0.5f) < 0.001f);
			Check($"an unmount writes Mount and Bolts ({off.Written})", off.Written == (PartFields.Mount | PartFields.Bolts));

			var unmountedStored = Sub(unmounted: true, condition: 0.5f);
			var staleExamine = Sub(unmounted: false, condition: 0.5f, examined: true, changed: PartFields.Examined);
			var dropped = PartRecordMerge.Normalise(unmountedStored, staleExamine, null);
			Check($"a stale examine (base Unmounted differs) is dropped ({dropped.Outcome}, stale {dropped.Stale})", dropped.Outcome == MergeOutcome.StaleDropped && dropped.Record == null && dropped.Stale == PartFields.Mount);

			var replaced = Sub(unmounted: false, id: "disc_other", condition: 1f);
			var onReplaced = PartRecordMerge.Normalise(replaced, staleExamine, null);
			Check($"an examine of a replaced part (base PartId differs) is dropped ({onReplaced.Outcome}, stale {onReplaced.Stale})", onReplaced.Outcome == MergeOutcome.StaleDropped && onReplaced.Stale == PartFields.Identity);

			var otherQuality = Sub(unmounted: false, condition: 1f, quality: 4);
			var conditionEdit = Sub(unmounted: false, condition: 0.2f, quality: 1, changed: PartFields.Condition);
			var onOtherQuality = PartRecordMerge.Normalise(otherQuality, conditionEdit, null);
			Check($"a condition change of a part replaced by one of another quality is dropped ({onOtherQuality.Outcome}, stale {onOtherQuality.Stale})", onOtherQuality.Outcome == MergeOutcome.StaleDropped && onOtherQuality.Stale == PartFields.Quality);

			var examine = Sub(unmounted: false, condition: 0.3f, examined: true, changed: PartFields.Examined);
			var merged = PartRecordMerge.Normalise(stored, examine, null);
			Check("a matching base merges the masked groups", merged.Record != null && merged.Record.IsExamined);
			Check("a matching base keeps the unmasked groups", merged.Record != null && Math.Abs(merged.Record.Condition - 0.5f) < 0.001f);
			Check($"a sender stale in an unmasked group is counted staleMerged ({merged.Outcome})", merged.Outcome == MergeOutcome.StaleMerged);
			Check($"the merge writes Examined ({merged.Written})", merged.Written == PartFields.Examined);

			var fresh = PartRecordMerge.Normalise(stored, Sub(unmounted: false, condition: 0.5f, examined: true, changed: PartFields.Examined), null);
			Check($"a sender with the stored base and values merges plainly ({fresh.Outcome})", fresh.Outcome == MergeOutcome.Merged);

			var tune = Sub(unmounted: false, condition: 0.5f, tuned: "disc_sport", changed: PartFields.Identity);
			var tuned = PartRecordMerge.Normalise(stored, tune, null);
			Check($"an Identity-only tune merges ({tuned.Outcome}, {tuned.Written})", tuned.Outcome == MergeOutcome.Merged && tuned.Record.TunedID == "disc_sport" && tuned.Written == PartFields.Identity);

			var corrupt = Sub(unmounted: true, condition: 0.1f, changed: PartFields.All);
			var all = PartRecordMerge.Normalise(stored, corrupt, null);
			Check("All without a precondition is taken whole", all.Outcome == MergeOutcome.Whole && all.Written == PartFields.All && all.Record.Unmounted && Math.Abs(all.Record.Condition - 0.1f) < 0.001f);

			var zero = PartRecordMerge.Normalise(stored, Sub(unmounted: true), null);
			Check("a 0 mask is skipped", zero.Outcome == MergeOutcome.Skipped && zero.Record == null);

			var sameExamined = PartRecordMerge.Normalise(Sub(unmounted: false, examined: true), Sub(unmounted: false, examined: false, changed: PartFields.Examined), null);
			Check($"an examine never clears IsExamined and writes nothing ({sameExamined.Written})", sameExamined.Record.IsExamined && sameExamined.Written == PartFields.None);

			var body = Body(switched: true, condition: 0.4f);
			var weld = Body(switched: true, condition: 0.9f, changed: PartFields.Condition);
			var welded = PartRecordMerge.Normalise(body, weld, null);
			Check($"a body condition change merges and keeps the stored Switched ({welded.Outcome}, {welded.Written})", welded.Outcome == MergeOutcome.Merged && welded.Record.Switched
				&& Math.Abs(welded.Record.State.Condition - 0.9f) < 0.001f && welded.Written == PartFields.Condition);
			var staleSwitch = PartRecordMerge.Normalise(body, Body(switched: false, condition: 0.9f, changed: PartFields.Condition), null);
			Check($"a body change with another Switched base is dropped ({staleSwitch.Outcome}, stale {staleSwitch.Stale})", staleSwitch.Outcome == MergeOutcome.StaleDropped && staleSwitch.Stale == PartFields.Switched);
			var bodyQuality = PartRecordMerge.Normalise(body, Body(switched: true, condition: 0.4f, quality: 2, changed: PartFields.Quality), null);
			Check($"a body quality change keeps the stored condition ({bodyQuality.Written})", bodyQuality.Record.State.Quality == 2 && Math.Abs(bodyQuality.Record.State.Condition - 0.4f) < 0.001f && bodyQuality.Written == PartFields.Quality);
			var bodyOff = PartRecordMerge.Normalise(body, Body(switched: true, unmounted: true, condition: 0.4f, changed: PartFields.Mount), Pre(false));
			Check($"a body unmount writes Mount ({bodyOff.Written})", bodyOff.Record.Unmounted && bodyOff.Written == PartFields.Mount);

			Check("Differ reports the groups that differ", PartRecordMerge.Differ(stored, Sub(unmounted: true, condition: 0.9f, examined: true)) == (PartFields.Mount | PartFields.Condition | PartFields.Examined));
			Check("Differ compares bolts element-wise within 0.001", PartRecordMerge.Differ(Sub(unmounted: false, bolts: 0.5f), Sub(unmounted: false, bolts: 0.5004f)) == PartFields.None);
		}

		private static void CheckDetails()
		{
			var stored = Details();
			DetailsMerge.Merge(stored, new ModCarDetails { Fluids = new List<ModFluidLevel> { new ModFluidLevel { Type = ModCarFluidType.Brake, Id = 0, Level = 0.2f } } }, 0, AlignmentFields.None);
			DetailsMerge.Merge(stored, new ModCarDetails { Fluids = new List<ModFluidLevel> { new ModFluidLevel { Type = ModCarFluidType.EngineCoolant, Id = 0, Level = 0.7f } } }, 0, AlignmentFields.None);
			Check("two fluids written by two updates are both kept", Level(stored, ModCarFluidType.Brake) == 0.2f && Level(stored, ModCarFluidType.EngineCoolant) == 0.7f && stored.Fluids.Count == 3);

			var wheels = Details().Wheels;
			wheels[0].ET = 99;
			wheels[3].ET = 33;
			DetailsMerge.Merge(stored, new ModCarDetails { Wheels = wheels }, 1 << 3, AlignmentFields.None);
			Check("a wheel mask merges only its wheels", stored.Wheels[3].ET == 33 && stored.Wheels[0].ET == 0);
			var all = Details().Wheels;
			all[1].ET = 11;
			DetailsMerge.Merge(stored, new ModCarDetails { Wheels = all }, 0, AlignmentFields.None);
			Check("wheel mask 0 means every wheel", stored.Wheels[1].ET == 11 && stored.Wheels[3].ET == 0);
			var grown = new ModCarWheel[6];
			grown[5] = new ModCarWheel { ET = 55 };
			DetailsMerge.Merge(stored, new ModCarDetails { Wheels = grown }, 1 << 5, AlignmentFields.None);
			Check("a masked wheel beyond the stored array grows it", stored.Wheels.Length == 6 && stored.Wheels[5].ET == 55 && stored.Wheels[1].ET == 11);

			DetailsMerge.Merge(stored, new ModCarDetails { Alignment = new ModAlignment { FL = 0.5f, RR = 0.9f } }, 0, AlignmentFields.FL);
			Check("an alignment mask merges only its fields", stored.Alignment.FL == 0.5f && stored.Alignment.RR == 0.1f);
			DetailsMerge.Merge(stored, new ModCarDetails { Alignment = new ModAlignment { FL = 0.5f, RR = 0.9f } }, 0, AlignmentFields.RR);
			Check("a second alignment write of another field keeps the first", stored.Alignment.FL == 0.5f && stored.Alignment.RR == 0.9f);

			DetailsMerge.Merge(stored, new ModCarDetails { BodyCosmetics = new List<ModBodyCosmetics> { new ModBodyCosmetics { PartIndex = 1, Dust = 0.8f } } }, 0, AlignmentFields.None);
			Check("a body panel merges per panel", stored.BodyCosmetics.Count == 2 && stored.BodyCosmetics.First(p => p.PartIndex == 1).Dust == 0.8f && stored.BodyCosmetics.First(p => p.PartIndex == 0).Dust == 0.3f);

			DetailsMerge.Merge(stored, new ModCarDetails { Tuning = new ModCarTuning { Modules = new List<ModPartTuning> { new ModPartTuning { PartKey = "s:2", EcuStage = 2 } } } }, 0, AlignmentFields.None);
			Check("a tuning module merges per part and keeps the gearbox", stored.Tuning.Modules.Count == 2 && stored.Tuning.Gearbox != null);

			var sent = Details();
			sent.Wheels[2].ET = 22;
			var only = DetailsMerge.Only(sent, 0, AlignmentFields.None, new HashSet<string> { "f:Brake.0", "w:2", "a:RL", "c:1" });
			Check($"Only keeps the named entries with their masks (wheels {only.WheelMask}, alignment {only.AlignmentMask})", only.Details.Fluids.Count == 1 && only.WheelMask == 1 << 2
				&& only.AlignmentMask == AlignmentFields.RL && only.Details.BodyCosmetics.Count == 1 && only.Details.Tuning == null && only.Details.Info == null);
			var carried = DetailsMerge.CarriedSignatures(only.Details, only.WheelMask, only.AlignmentMask);
			Check($"the carried entries follow the masks ({string.Join(",", carried.Keys)})", string.Join(",", carried.Keys) == "a:RL,c:1,f:Brake.0,w:2");
		}

		private static ModCarDetails Details() => new ModCarDetails
		{
			Fluids = new List<ModFluidLevel>
			{
				new ModFluidLevel { Type = ModCarFluidType.Brake, Id = 0, Level = 0.5f },
				new ModFluidLevel { Type = ModCarFluidType.EngineCoolant, Id = 0, Level = 0.5f },
				new ModFluidLevel { Type = ModCarFluidType.EngineOil, Id = 0, Level = 0.5f },
			},
			Wheels = Enumerable.Range(0, 4).Select(_ => new ModCarWheel { Width = 195, RimSize = 15, TireSize = 65 }).ToArray(),
			Alignment = new ModAlignment { FL = 0.1f, FR = 0.1f, RL = 0.1f, RR = 0.1f },
			BodyCosmetics = new List<ModBodyCosmetics> { new ModBodyCosmetics { PartIndex = 0, Dust = 0.3f }, new ModBodyCosmetics { PartIndex = 1, Dust = 0.3f } },
			Tuning = new ModCarTuning { Gearbox = new ModGearboxData { FinalDriveRatio = 3f }, Modules = new List<ModPartTuning> { new ModPartTuning { PartKey = "s:1" } } },
			Info = new ModCarInfo { Mileage = 100 },
		};

		// state-merges-and-contention D11: each new digest key projects the server's stored state and the client's DTOs
		// (other list order, float noise, the client's extra empty or locked entries) to the same hash.
		private static void CheckDigests()
		{
			var state = new ModGameState();
			var spawn = new CarLoaderEntry { SpawnSeq = 3, HasBaseline = true };
			state.CarState.LoadedCars[0] = spawn;
			var stored = Details();
			stored.SpawnSeq = 3;
			stored.HasSnapshot = true;
			stored.Fluids.Reverse();
			stored.Info.BuyPrice = 5000;
			state.CarState.Details[0] = stored;
			var read = Details();
			read.Fluids[0].Level += 0.0003f;
			read.Info.BuyPrice = 4000;
			SameHash("car-details", state, DigestMappers.DetailsKey, "0", DigestMappers.Details(read));
			var otherCoolant = Details();
			otherCoolant.Fluids.First(f => f.Type == ModCarFluidType.EngineCoolant).Level = 0.9f;
			DifferentHash("car-details", state, DigestMappers.DetailsKey, "0", DigestMappers.Details(otherCoolant));
			Check("car-details of an old SpawnSeq is not compared", ReconciliationService.ProjectState(Reseq(state, 4), DigestMappers.DetailsKey, "0") == null);

			var wheel = new ModGroupItem { ID = "wheel_1", UID = 77, ItemList = new List<ModItem> { new ModItem { ID = "rim_1", UID = 78, Condition = 0.8f } } };
			state.ToolsState.Slots[ModToolId.TireChanger] = new ToolSlotState { Tool = ModToolId.TireChanger, Group = wheel, Mounting = true, Angle = 30f };
			state.ToolsState.Slots[ModToolId.BrakeLathe] = new ToolSlotState { Tool = ModToolId.BrakeLathe, Item = new ModItem { ID = "disc_1", UID = 79 } };
			state.ToolsState.Slots[ModToolId.SpringClamp] = ToolSlotState.Empty(ModToolId.SpringClamp);
			var local = new List<ToolSlotState>
			{
				new ToolSlotState { Tool = ModToolId.BrakeLathe, Item = new ModItem { ID = "disc_1", UID = 79, Condition = 0.2f } },
				ToolSlotState.Empty(ModToolId.WheelBalancer),
				new ToolSlotState { Tool = ModToolId.EngineStand1, Group = new ModGroupItem() },
				new ToolSlotState { Tool = ModToolId.TireChanger, Group = wheel, Mounting = true },
			};
			SameHash("workshop-tools", state, DigestMappers.ToolsKey, "", DigestMappers.Tools(local));
			local[2] = new ToolSlotState { Tool = ModToolId.TireChanger, Group = wheel, Mounting = false };
			DifferentHash("workshop-tools", state, DigestMappers.ToolsKey, "", DigestMappers.Tools(local));

			state.InventoryState.WarehouseItems.Add(new ModItem { ID = "door_1", UID = 1, Condition = 0.5f });
			state.InventoryState.WarehouseItems.Add(new ModItem { ID = "hood_1", UID = 2, Condition = 0.25f });
			state.InventoryState.WarehouseGroupItems.Add(wheel);
			var warehouseItems = new List<ModItem> { new ModItem { ID = "hood_1", UID = 2, Condition = 0.2502f }, new ModItem { ID = "door_1", UID = 1, Condition = 0.5f } };
			SameHash("warehouse", state, DigestMappers.WarehouseKey, "", DigestMappers.Warehouse(warehouseItems, new[] { wheel }));
			DifferentHash("warehouse", state, DigestMappers.WarehouseKey, "", DigestMappers.Warehouse(warehouseItems.Take(1), new[] { wheel }));

			state.GarageState.GarageUpgradeLevels["lifter"] = new[] { true, false };
			state.GarageState.PlayerUpgradeLevels["faster_unmount"] = new[] { true, true, false };
			state.WorldState.Barns = 2;
			var upgrades = new Dictionary<string, bool[]> { ["carwash"] = new[] { false }, ["lifter"] = new[] { true, false, false } };
			var skills = new Dictionary<string, bool[]> { ["faster_unmount"] = new[] { true, true, false }, ["cheaper_parking"] = new[] { false } };
			SameHash("garage", state, DigestMappers.GarageKey, "", DigestMappers.Garage(upgrades, skills, 2));
			skills["cheaper_parking"][0] = true;
			DifferentHash("garage", state, DigestMappers.GarageKey, "", DigestMappers.Garage(upgrades, skills, 2));

			state.JobsState.Orders.Add(new OrderEntry { Job = new ModJob { id = 5 }, Status = OrderStatus.Claimed });
			state.JobsState.Orders.Add(new OrderEntry { Job = new ModJob { id = 4 } });
			state.JobsState.ActiveJobs.Add(new ActiveJobEntry { Job = new ModJob { id = 2, carLoaderID = -1 }, CarLoaderId = 1 });
			var orders = new[] { 4, 5 };
			SameHash("jobs", state, DigestMappers.JobsKey, "", DigestMappers.Jobs(orders, new[] { new KeyValuePair<int, int>(2, 1) }));
			DifferentHash("jobs", state, DigestMappers.JobsKey, "", DigestMappers.Jobs(orders, new[] { new KeyValuePair<int, int>(2, 0) }));
		}

		private static ModGameState Reseq(ModGameState state, int spawnSeq)
		{
			var copy = JsonConvert.DeserializeObject<ModGameState>(JsonConvert.SerializeObject(state));
			copy.CarState.LoadedCars[0].SpawnSeq = spawnSeq;
			return copy;
		}

		private static void SameHash(string key, ModGameState state, string digestKey, string subKey, Projection client)
		{
			var server = ReconciliationService.ProjectState(state, digestKey, subKey);
			Check($"{key}: the server's stored state and the client's DTOs give the same hash", server != null && server.Hash() == client.Hash());
		}

		private static void DifferentHash(string key, ModGameState state, string digestKey, string subKey, Projection client)
		{
			var server = ReconciliationService.ProjectState(state, digestKey, subKey);
			Check($"{key}: one changed field gives another hash", server != null && server.Hash() != client.Hash());
		}

		private static float Level(ModCarDetails details, ModCarFluidType type) => details.Fluids.First(f => f.Type == type).Level;

		private static PartPrecondition Pre(bool wasUnmounted) => new PartPrecondition { Key = "s:3.22.4", WasUnmounted = wasUnmounted };

		private static CarSubPartUpdatePacket Sub(bool unmounted, string id = "disc", string tuned = "", float condition = 0.5f, int quality = 1,
			bool examined = false, float bolts = 1f, PartFields changed = PartFields.None) => new CarSubPartUpdatePacket
		{
			PartIndexPath = new[] { 3, 22, 4 }, PartId = id, TunedID = tuned, Unmounted = unmounted, Condition = condition, Quality = quality,
			IsExamined = examined, Dust = 0.1f, Changed = changed,
			MountObjectData = new ModMountObjectData { ParentPath = "p", Condition = new[] { bolts, 1f }, IsStuck = new[] { false, false } },
		};

		private static CarBodyPartUpdatePacket Body(bool switched, float condition, int quality = 1, bool unmounted = false, PartFields changed = PartFields.None) =>
			new CarBodyPartUpdatePacket
			{
				PartIndex = 2, PartName = "door_front_left", Switched = switched, Unmounted = unmounted, TunedID = "",
				State = new ModItem { ID = "door_front_left", Condition = condition, Dent = 0.1f, Quality = quality }, Changed = changed,
			};

		private static void Check(string what, bool ok)
		{
			Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
			if (!ok) failures++;
		}
	}
}
