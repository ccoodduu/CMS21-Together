using System;
using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Placement;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class ParkingHandlers
	{
		private const int BaseParkingLevelPrice = 50000;

		[PacketHandler(PacketTypes.CarParkRequest)]
		public static void OnPark(long clientId, CarParkRequestPacket request)
		{
			int client = (int)clientId;
			if (request.Car?.Data == null || request.Car.Data.Length == 0)
			{
				Reply(client, request, ParkRefusal.Invalid);
				return;
			}
			if (request.CarLoaderID >= 0) ParkFromGarage(client, request);
			else ParkArrival(client, request);
		}

		private static void ParkFromGarage(int client, CarParkRequestPacket request)
		{
			var entry = CarPartsStore.Get(request.CarLoaderID);
			ParkRefusal refusal = ParkRefusal.None;
			if (request.Price != 0 || entry == null) refusal = ParkRefusal.Invalid;
			else if (entry.Spawn.IsJob) refusal = ParkRefusal.JobCar;
			int slot = -1;
			if (refusal == ParkRefusal.None && !ParkingService.TryAdd(request.Car, request.PreferredSlot, out slot)) refusal = ParkRefusal.ParkingFull;

			if (refusal != ParkRefusal.None)
			{
				Logger.Info($"[Parking] Park of loader {request.CarLoaderID} from client {client} refused: {refusal}.");
				Reply(client, request, refusal);
				if (request.PreferredSlot >= 0) ParkingService.BroadcastSlot(request.PreferredSlot, client);
				if (entry == null) return;
				var giveBack = entry.Spawn;
				giveBack.CarData = request.Car.Data;
				giveBack.CarDataVersion = (byte)request.Car.SaveVersion;
				Server.SendToClient(giveBack, client);
				if (entry.HasBaseline) CarPartsStore.SendSnapshot(request.CarLoaderID, entry, CarPartsSnapshotPacket.LiveSnapshot, only: client);
				return;
			}

			CarPartsStore.ClearLoader(request.CarLoaderID, ClearReason.Parked);
			Server.SendToClients(new CarSpawnDeletePacket { CarLoaderID = request.CarLoaderID }, client);
			Logger.Info($"[Parking] Loader {request.CarLoaderID} ({request.Car.CarToLoad}) parked in slot {slot} by client {client}.");
			ParkingService.BroadcastSlot(slot);
			if (request.PreferredSlot >= 0 && request.PreferredSlot != slot) ParkingService.BroadcastSlot(request.PreferredSlot, client);
			Reply(client, request, ParkRefusal.None, slot);
		}

		private static void ParkArrival(int client, CarParkRequestPacket request)
		{
			var world = GameDataManager.CurrentState.WorldState;
			ParkRefusal refusal = ParkRefusal.None;
			if (request.Price < 0) refusal = ParkRefusal.Invalid;
			else if (request.Price > world.Money) refusal = ParkRefusal.NoMoney;
			int slot = -1;
			if (refusal == ParkRefusal.None && !ParkingService.TryAdd(request.Car, request.PreferredSlot, out slot)) refusal = ParkRefusal.ParkingFull;
			if (refusal != ParkRefusal.None)
			{
				Logger.Info($"[Parking] Arrival of {request.Car.CarToLoad} from client {client} refused: {refusal}.");
				Reply(client, request, refusal);
				return;
			}

			Logger.Info($"[Parking] {request.Car.CarToLoad} arrived in slot {slot} from client {client} for {request.Price}.");
			ParkingService.BroadcastSlot(slot);
			if (request.Price > 0)
			{
				world.Money -= request.Price;
				Server.SendToClients(world);
			}
			Reply(client, request, ParkRefusal.None, slot);
		}

		[PacketHandler(PacketTypes.CarUnparkRequest)]
		public static void OnUnpark(long clientId, CarUnparkRequestPacket request)
		{
			int client = (int)clientId;
			var car = ParkingService.Get(request.Slot);
			string refusal = null;
			if (car == null || car.Id != request.ParkedCarId) refusal = "the slot no longer holds that car";
			else if (CarPartsStore.Get(request.CarLoaderID) != null) refusal = $"loader {request.CarLoaderID} is in use";
			else if (PlacementRules.LoaderAtPlace(request.Place).HasValue) refusal = $"place {request.Place} is taken";
			if (refusal != null)
			{
				Logger.Info($"[Parking] Unpark of slot {request.Slot} from client {client} refused: {refusal}.");
				Server.SendToClient(new CarSpawnRejectedPacket { CarLoaderID = request.CarLoaderID, Reason = $"Taking the car out of parking failed: {refusal}." }, client);
				ParkingService.BroadcastSlot(request.Slot, client);
				return;
			}

			ParkingService.TryRemove(request.Slot, car.Id);
			var entry = CarPartsStore.RegisterSpawn(new CarSpawnResponsePacket
			{
				CarLoaderID = request.CarLoaderID,
				CarToLoad = car.CarToLoad,
				ConfigVersion = request.ConfigVersion,
				PlaceNo = request.Place,
				JobID = -1,
				CarData = car.Data,
				CarDataVersion = (byte)car.SaveVersion
			}, client);
			entry.FromParking = car;
			Server.SendToClients(entry.Spawn, client);
			Logger.Info($"[Parking] Slot {request.Slot} ({car.CarToLoad}) unparked to loader {request.CarLoaderID} by client {client}.");
			ParkingService.BroadcastSlot(request.Slot);
		}

		[PacketHandler(PacketTypes.ParkingMoveRequest)]
		public static void OnMove(long clientId, ParkingMoveRequestPacket request)
		{
			if (ParkingService.Swap(request.From, request.To, request.FromId, request.ToId))
			{
				Logger.Info($"[Parking] Slots {request.From} and {request.To} swapped by client {clientId}.");
				ParkingService.BroadcastSlot(request.From);
				ParkingService.BroadcastSlot(request.To);
				return;
			}
			Logger.Info($"[Parking] Swap of slots {request.From} and {request.To} from client {clientId} refused.");
			ParkingService.BroadcastSlot(request.From, (int)clientId);
			ParkingService.BroadcastSlot(request.To, (int)clientId);
		}

		[PacketHandler(PacketTypes.ParkingResyncRequest)]
		public static void OnResync(long clientId, ParkingResyncRequestPacket request)
		{
			Logger.Info($"[Parking] Resync for client {clientId}.");
			ParkingService.SendState((int)clientId);
		}

		[PacketHandler(PacketTypes.ParkingLevelUnlockRequest)]
		public static void OnUnlock(long clientId, ParkingLevelUnlockRequestPacket request)
		{
			var lot = GameDataManager.CurrentState.PlacementState.Parking;
			var world = GameDataManager.CurrentState.WorldState;
			int fullPrice = lot.UnlockedLevels * BaseParkingLevelPrice;
			bool priceOk = request.Price == fullPrice || request.Price == fullPrice / 2;
			if (request.TargetLevels != lot.UnlockedLevels + 1 || request.TargetLevels > ParkingLayout.MaxLevels || !priceOk || world.Money < request.Price)
			{
				Logger.Info($"[Parking] Unlock of level {request.TargetLevels} for {request.Price} from client {clientId} refused (levels {lot.UnlockedLevels}, money {world.Money}).");
				ParkingService.SendState((int)clientId);
				return;
			}
			lot.UnlockedLevels = request.TargetLevels;
			world.Money -= request.Price;
			Logger.Info($"[Parking] Level {request.TargetLevels} unlocked for {request.Price} by client {clientId}.");
			Server.SendToClients(world);
			ParkingService.SendState();
		}

		private static void Reply(int client, CarParkRequestPacket request, ParkRefusal reason, int slot = -1) =>
			Server.SendToClient(new CarParkResultPacket { RequestId = request.RequestId, Accepted = reason == ParkRefusal.None, Reason = reason, Slot = slot }, client);
	}
}
