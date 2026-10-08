using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Placement
{
	public static class ParkingService
	{
		private static ParkingLot Lot => GameDataManager.CurrentState.PlacementState.Parking;

		public static int UsableSlots => ParkingLayout.UsableSlots(Lot.UnlockedLevels);

		public static bool IsUsable(int slot) => slot >= 0 && slot < UsableSlots;

		public static ParkedCar Get(int slot) => Lot.Slots.TryGetValue(slot, out var car) ? car : null;

		public static bool TryAdd(ParkedCar car, int preferredSlot, out int slot)
		{
			if (car.Id == Guid.Empty) car.Id = Guid.NewGuid();
			slot = IsUsable(preferredSlot) && Get(preferredSlot) == null ? preferredSlot : FirstFree();
			if (slot < 0) return false;
			Lot.Slots[slot] = car;
			return true;
		}

		private static int FirstFree()
		{
			for (int slot = 0; slot < UsableSlots; slot++)
				if (Get(slot) == null) return slot;
			return -1;
		}

		public static bool TryRemove(int slot, Guid id)
		{
			var car = Get(slot);
			if (car == null || car.Id != id) return false;
			Lot.Slots.Remove(slot);
			Lot.Records.Remove(id);
			return true;
		}

		public static void KeepRecord(ParkedRecord record) => Lot.Records[record.CarId] = record;

		public static ParkedRecord TakeRecord(Guid id)
		{
			if (!Lot.Records.TryGetValue(id, out var record)) return null;
			Lot.Records.Remove(id);
			return record;
		}

		public static bool Swap(int from, int to, Guid fromId, Guid toId)
		{
			if (!IsUsable(from) || !IsUsable(to) || from == to) return false;
			var fromCar = Get(from);
			var toCar = Get(to);
			if ((fromCar?.Id ?? Guid.Empty) != fromId || (toCar?.Id ?? Guid.Empty) != toId) return false;
			if (fromCar == null) Lot.Slots.Remove(to); else Lot.Slots[to] = fromCar;
			if (toCar == null) Lot.Slots.Remove(from); else Lot.Slots[from] = toCar;
			return true;
		}

		public static void BroadcastSlot(int slot, int only = CarLoaderEntry.NoClient)
		{
			var packet = new ParkingSlotUpdatePacket { Slot = slot, Car = Get(slot) };
			if (only == CarLoaderEntry.NoClient) Server.SendToClients(packet);
			else Server.SendToClient(packet, only);
		}

		public static int SendState(int only = CarLoaderEntry.NoClient)
		{
			var state = new ParkingStatePacket { UnlockedLevels = Lot.UnlockedLevels };
			foreach (var pair in Lot.Slots) state.Occupied[pair.Key] = pair.Value.Id;
			if (only == CarLoaderEntry.NoClient) Server.SendToClients(state);
			else Server.SendToClient(state, only);
			foreach (int slot in Lot.Slots.Keys.OrderBy(s => s)) BroadcastSlot(slot, only);
			return 1 + Lot.Slots.Count;
		}

		public static IEnumerable<string> Describe()
		{
			yield return $"parking: {Lot.UnlockedLevels} levels, {Lot.Slots.Count}/{UsableSlots} slots used, {Lot.Records.Count} part records kept";
			foreach (var pair in Lot.Slots.OrderBy(p => p.Key))
				yield return $"  slot {pair.Key}: {pair.Value.CarToLoad} ({pair.Value.Data?.Length ?? 0} bytes, id {pair.Value.Id})";
		}
	}
}
