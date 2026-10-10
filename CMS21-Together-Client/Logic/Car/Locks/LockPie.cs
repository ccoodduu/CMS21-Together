using System.Collections.Generic;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Guard;

namespace CMS21Together.Logic.Car.Locks;

public static class LockPie
{
	public const string SourceName = "locks";
	private const int NoPlace = -1;

	public static readonly IReadOnlyDictionary<string, int> CarOptions = new Dictionary<string, int>
	{
		["move_car"] = NoPlace, ["move_parking"] = NoPlace, ["car_drive"] = NoPlace,
		["move_entrance1"] = (int)CarPlace.Entrance1, ["move_entrance2"] = (int)CarPlace.Entrance2, ["move_entrance3"] = (int)CarPlace.Entrance3,
		["move_carLift1"] = (int)CarPlace.CarLifter1, ["move_carLift2"] = (int)CarPlace.CarLifter2, ["move_paintshop"] = (int)CarPlace.Paintshop,
		["move_dyno"] = (int)CarPlace.Dyno, ["move_pathTest"] = (int)CarPlace.DiagnosticPath, ["move_carWash"] = (int)CarPlace.CarWash,
	};

	public static void Initialize()
	{
		PieOptionState.AddSource(new PieBlockSource
		{
			Name = SourceName, Reason = Reason,
			Clicked = (id, reason) =>
			{
				CarLockMirror.Count("blockedAtSelection.pieClick");
				LockMessages.Refuse(reason);
			},
		});
	}

	public static string Reason(string id)
	{
		if (!LockGate.Active || !CarOptions.TryGetValue(id, out int place)) return null;
		var carLoader = GameScript.Get()?.GetIOMouseOverCarLoader2();
		int loader = LockFluidHooks.LoaderOf(carLoader);
		if (loader < 0) return null;
		string message = CarBlocked(loader, place, out _);
		if (message != null)
		{
			CarLockMirror.Count("blockedAtSelection.pie");
			return message;
		}
		int other = place == NoPlace ? -1 : LockCarHooks.LoaderAtPlace(place, carLoader);
		if (other < 0 || CarBlocked(other, NoPlace, out int holder) == null) return null;
		CarLockMirror.Count("blockedAtSelection.pie");
		return holder >= 0 ? LockMessages.OtherCar(holder) : "The car there is moving.";
	}

	private static string CarBlocked(int loader, int place, out int holder)
	{
		if (CarMotion.IsMoving(loader, out holder)) return LockMessages.Moving(loader, holder);
		var set = LockSets.ForCar(loader, CarLockKind.Move);
		set.Place = place;
		var conflict = CarLockMirror.Conflict(set);
		holder = conflict?.Holder ?? -1;
		return conflict == null ? null : LockMessages.ForConflict(loader, conflict, LockKeys.Car);
	}
}
