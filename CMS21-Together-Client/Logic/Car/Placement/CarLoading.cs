using System.Collections;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Hook;
using UnityEngine;

namespace CMS21Together.Logic.Car.Placement;

public static class CarLoading
{
	private const float FileLoadSeconds = 60f;
	private const int CoroutineDone = -1;

	private static readonly HashSet<int> loading = new HashSet<int>();

	public static bool IsLoading(int loader) => loading.Contains(loader);

	public static IEnumerator Load(CarLoader carLoader, int loader, CarSpawnResponsePacket spawn)
	{
		carLoader.placeNo = spawn.PlaceNo;
		carLoader.ConfigVersion = spawn.ConfigVersion;
		carLoader.customerCar = spawn.IsJob;
		CarPlacementSync.ForgetPendingPlace(loader);
		CarSpawnHooks.Suppress(loader);
		loading.Add(loader);
		try
		{
			var data = spawn.CarData == null ? null : Decode(spawn);
			CarLoader._LoadCarFromFile_d__425 fromFile = null;
			if (data != null)
			{
				var routine = carLoader.LoadCarFromFile(data);
				fromFile = routine.TryCast<CarLoader._LoadCarFromFile_d__425>();
				carLoader.StartCoroutine(routine);
			}
			else carLoader.StartCoroutine(carLoader.LoadCar(spawn.CarToLoad));
			while (!carLoader.IsCarLoaded()) yield return new WaitForEndOfFrame();
			float deadline = Time.realtimeSinceStartup + FileLoadSeconds;
			while (fromFile != null && fromFile.__1__state != CoroutineDone && Time.realtimeSinceStartup < deadline) yield return new WaitForEndOfFrame();
			if (fromFile != null && fromFile.__1__state != CoroutineDone) Log.Warn($"[Placement] Loader {loader}: {spawn.CarToLoad} did not finish loading from its saved data in {FileLoadSeconds:0} s.");
			carLoader.PlaceAtPosition(true, true);
			if (spawn.PlaceNo < 0) carLoader.placeNo = spawn.PlaceNo;
			CarPlacementSync.ApplyPlace(carLoader, loader, CarPlacementSync.TakePendingPlace(loader, spawn.PlaceNo));
			if (spawn.IsJob) Jobs.JobsSync.MarkCustomerCar(loader, spawn.JobID);
			if (spawn.SpecialState > 0) carLoader.specialState = spawn.SpecialState;
		}
		finally
		{
			loading.Remove(loader);
			CarSpawnHooks.Release(loader);
		}
	}

	private static NewCarData Decode(CarSpawnResponsePacket spawn)
	{
		if (spawn.CarDataVersion != NewCarDataCodec.SaveVersion)
		{
			Log.Warn($"[Placement] {spawn.CarToLoad} was saved with save version {spawn.CarDataVersion}, this game uses {NewCarDataCodec.SaveVersion}; loading it by name.");
			return null;
		}
		return NewCarDataCodec.Deserialize(spawn.CarData, spawn.CarDataVersion);
	}
}
