using System.Linq;
using CMS21_Together_Core.Network.Packets;

namespace CMS21Together.Logic.Car.Locks;

public static class CarMotion
{
	public static bool IsMoving(int loader, out int holder)
	{
		holder = CarLockMirror.All.Where(r => r.Loader == loader && (r.Kind == CarLockKind.Lift || r.Kind == CarLockKind.Move)).Select(r => r.Owner).DefaultIfEmpty(-1).First();
		var lifters = GarageLoader.Get()?.carLifter;
		var places = CarLoaderPlaces.Get();
		for (int i = 0; lifters != null && places != null && i < lifters.Length; i++)
		{
			var lifter = lifters[i];
			var connected = lifter?.GetConnectedCarLoader();
			if (connected == null || places.GetCarLoaderId(connected) != loader) continue;
			if (lifter.isMoving || Placement.LifterSync.IsApplying(i)) return true;
		}
		return Placement.CarPlacementSync.IsApplying(loader);
	}
}
