using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Car.Placement;

namespace CMS21Together.Logic.Car.Locks;

public static class LockCarHooks
{
	public static bool Gate(GatedAction action) => action == null || !LockGate.Active || LockGate.Enter(action);

	public static GatedAction LiftAction(CarLifter lifter, int index, int actionType)
	{
		var carLoader = lifter.GetConnectedCarLoader();
		int loader = LockFluidHooks.LoaderOf(carLoader);
		if (loader < 0 || lifter.isMoving) return null;
		var set = LockSets.ForCar(loader, CarLockKind.Lift);
		int state = (int)lifter.GetState();
		int seq = CarPartsSync.SpawnSeq(loader);
		return new GatedAction
		{
			Set = set, Target = lifter, TargetKey = LockKeys.Car,
			Context = () => !lifter.isMoving && (int)lifter.GetState() == state && lifter.GetConnectedCarLoader() == carLoader && CarPartsSync.SpawnSeq(loader) == seq,
			Run = () => lifter.Action(actionType),
			Started = () => lifter.isMoving,
			OnStarted = lockId => LockLifecycle.Track(lockId, set, LockKeys.Car, finished: () => !lifter.isMoving),
		};
	}

	public static GatedAction MoveAction(CarLoader carLoader, CarPlace pos, bool movePlayerToCar)
	{
		int loader = LockFluidHooks.LoaderOf(carLoader);
		if (loader < 0) return null;
		int from = carLoader.GetPlaceNo();
		int other = LoaderAtPlace((int)pos, carLoader);
		var set = LockSets.ForCar(loader, CarLockKind.Move);
		var mode = GameMode.Get();
		var previous = mode != null ? mode.previousMode : gameMode.Garage;
		int seq = CarPartsSync.SpawnSeq(loader);
		return new GatedAction
		{
			Set = set, Target = carLoader, TargetKey = LockKeys.Car, OtherLoader = other,
			Context = () => carLoader != null && carLoader.GetPlaceNo() == from && CarPartsSync.SpawnSeq(loader) == seq,
			Run = () =>
			{
				if (mode != null) mode.previousMode = previous;
				var center = NotificationCenter.Get();
				center.StartCoroutine(center.ChangeCarPos(carLoader, pos, movePlayerToCar));
			},
			Started = () => CarPlacementSync.IsMovingLocally(loader),
			OnStarted = lockId => LockLifecycle.Track(lockId, set, LockKeys.Car, finished: () => !CarPlacementSync.IsMovingLocally(loader)),
		};
	}

	public static int LoaderAtPlace(int place, CarLoader except)
	{
		var places = CarLoaderPlaces.Get();
		for (int i = 0; places != null && i < places.GetCarLoadersCount(); i++)
		{
			var carLoader = places.GetCarLoaderByIndex(i);
			if (carLoader == null || carLoader == except || string.IsNullOrEmpty(carLoader.carToLoad)) continue;
			if (carLoader.GetPlaceNo() == place) return i;
		}
		return -1;
	}
}
