using System.Collections;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;
using CMS21Together.UI;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Car.Placement;

// Developer shortcut (DevHotkeys, F6): until orders (row 3) and purchases (row 6) are synced there is no other way to
// get a car into a connected garage for a playtest.
public static class DevCarSpawner
{
	private static readonly CarPlace[] Places = { CarPlace.Entrance1, CarPlace.Entrance2, CarPlace.Entrance3, CarPlace.CarLifter1, CarPlace.CarLifter2 };

	public static void SpawnRandom()
	{
		if (!ClientScene.IsGarageReady || !ClientData.IsInitialSyncFinished) return;
		var places = CarLoaderPlaces.Get();
		CarLoader free = null;
		for (int i = 0; i < places.GetCarLoadersCount() && free == null; i++)
		{
			var carLoader = places.GetCarLoaderByIndex(i);
			if (carLoader != null && string.IsNullOrEmpty(carLoader.carToLoad)) free = carLoader;
		}
		CarPlace? place = null;
		foreach (var candidate in Places)
			if (place == null && places.GetCarLoaderForPlace(candidate) == null) place = candidate;
		string car = RandomBaseGameCar();
		if (free == null || place == null || car == null)
		{
			ModNotify.ShowToast("No free car place for a test car.");
			return;
		}
		MelonCoroutines.Start(Spawn(free, place.Value, car));
	}

	private static IEnumerator Spawn(CarLoader carLoader, CarPlace place, string car)
	{
		Log.Info($"[Dev] Spawning {car} at {place}.");
		carLoader.placeNo = (int)place;
		carLoader.StartCoroutine(carLoader.LoadCar(car));
		while (!carLoader.IsCarLoaded()) yield return null;
		carLoader.ResetCarLifter();
		carLoader.ChangePosition((int)place);
	}

	private static string RandomBaseGameCar()
	{
		var bundles = Object.FindObjectOfType<CarBundleLoader>();
		var cars = new List<string>();
		for (int i = 0; bundles?.CarNamesData != null && i < bundles.CarNamesData.Count; i++)
		{
			var data = bundles.CarNamesData[i];
			if (data != null && CarDlc.For(data.CarID) == CarDlc.BaseGame) cars.Add(data.CarID);
		}
		return cars.Count == 0 ? null : cars[Random.Range(0, cars.Count)];
	}
}
