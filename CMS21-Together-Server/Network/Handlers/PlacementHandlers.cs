using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Placement;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class PlacementHandlers
	{
		[PacketHandler(PacketTypes.LifterActionRequest)]
		public static void OnLifterAction(long clientId, LifterActionRequestPacket request)
		{
			int lifter = request.LifterIndex;
			int stored = PlacementRules.LifterState(lifter);
			bool oneStep = request.ToState >= PlacementRules.OnFloor && request.ToState <= PlacementRules.Up && System.Math.Abs(request.ToState - request.FromState) == 1;
			bool hasCar = PlacementRules.LoaderAtPlace(PlacementRules.PlaceOfLifter(lifter)) != null;
			int? liftedCar = PlacementRules.LoaderAtPlace(PlacementRules.PlaceOfLifter(lifter));
			if (liftedCar.HasValue && CarAwayRegistry.Blocks(liftedCar.Value, (int)clientId, $"lift {lifter}"))
			{
				PlacementRules.SendLifter(lifter, instant: false, only: (int)clientId);
				return;
			}
			if (PlacementRules.PlaceOfLifter(lifter) < 0 || request.FromState != stored || !oneStep || !hasCar)
			{
				Logger.Info($"[Placement] Lift {lifter} {request.FromState}->{request.ToState} from client {clientId} refused (stored {stored}, car {hasCar}).");
				PlacementRules.SendLifter(lifter, instant: false, only: (int)clientId);
				return;
			}
			PlacementRules.SetLifter(lifter, request.ToState);
			Logger.Info($"[Placement] Lift {lifter} {request.FromState}->{request.ToState} by client {clientId}.");
			PlacementRules.SendLifter(lifter, instant: false);
		}

		[PacketHandler(PacketTypes.CarPlaceChangeRequest)]
		public static void OnCarPlaceChange(long clientId, CarPlaceChangeRequestPacket request)
		{
			var entry = CarPartsStore.Get(request.CarLoaderID);
			if (entry?.Spawn == null)
			{
				Logger.Debug($"[Placement] Move of unknown loader {request.CarLoaderID} from client {clientId} dropped.");
				return;
			}

			int stored = entry.Spawn.PlaceNo;
			int? other = PlacementRules.LoaderAtPlace(request.ToPlace);
			string refusal = null;
			if (CarAwayRegistry.Blocks(request.CarLoaderID, (int)clientId, "move")) refusal = "the car is away";
			else if (other.HasValue && CarAwayRegistry.Blocks(other.Value, (int)clientId, "swap")) refusal = $"the car on place {request.ToPlace} is away";
			else if (request.FromPlace != stored) refusal = $"the car is at place {stored}";
			else if (request.ToPlace == stored) refusal = "the car is already there";
			else if (other.HasValue && IsRaised(request.FromPlace)) refusal = "a swap with a car on a raised lift";
			else if (other.HasValue && IsRaised(request.ToPlace)) refusal = $"the car on place {request.ToPlace} stands on a raised lift";
			if (refusal != null)
			{
				Logger.Info($"[Placement] Move of loader {request.CarLoaderID} {request.FromPlace}->{request.ToPlace} from client {clientId} refused: {refusal}.");
				Server.SendToClient(new CarPlaceChangedPacket { CarLoaderID = request.CarLoaderID, Place = stored }, (int)clientId);
				return;
			}

			PlacementRules.ResetLifterAt(stored);
			entry.Spawn.PlaceNo = request.ToPlace;
			entry.Spawn.SpecialState = 0;
			if (other.HasValue)
			{
				CarPartsStore.Get(other.Value).Spawn.PlaceNo = stored;
				Logger.Info($"[Placement] Loader {request.CarLoaderID} swapped places with loader {other.Value} ({stored}<->{request.ToPlace}) by client {clientId}.");
				Server.SendToClients(new CarPlaceChangedPacket { CarLoaderID = other.Value, Place = stored });
			}
			else
			{
				PlacementRules.ResetLifterAt(request.ToPlace);
				Logger.Info($"[Placement] Loader {request.CarLoaderID} moved {stored}->{request.ToPlace} by client {clientId}.");
			}
			Server.SendToClients(new CarPlaceChangedPacket { CarLoaderID = request.CarLoaderID, Place = request.ToPlace });
		}

		private static bool IsRaised(int place)
		{
			int lifter = PlacementRules.LifterAtPlace(place);
			return lifter >= 0 && PlacementRules.LifterState(lifter) != PlacementRules.OnFloor;
		}
	}
}
