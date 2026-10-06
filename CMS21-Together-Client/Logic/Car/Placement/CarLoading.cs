using System.Collections;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Hook;
using UnityEngine;

namespace CMS21Together.Logic.Car.Placement;

public static class CarLoading
{
	public static IEnumerator Load(CarLoader carLoader, int loader, CarSpawnResponsePacket spawn)
	{
		carLoader.placeNo = spawn.PlaceNo;
		carLoader.ConfigVersion = spawn.ConfigVersion;
		carLoader.customerCar = spawn.IsJob;
		CarSpawnHooks.Suppress(loader);
		try
		{
			var data = spawn.CarData == null ? null : Decode(spawn);
			if (data != null) carLoader.StartCoroutine(carLoader.LoadCarFromFile(data));
			else carLoader.StartCoroutine(carLoader.LoadCar(spawn.CarToLoad));
			while (!carLoader.IsCarLoaded()) yield return new WaitForEndOfFrame();
			carLoader.PlaceAtPosition(true, true);
			if (spawn.PlaceNo < 0) carLoader.placeNo = spawn.PlaceNo;
			CarPlacementSync.ApplyPlace(carLoader, loader, spawn.PlaceNo);
			if (spawn.IsJob) Jobs.JobsSync.MarkCustomerCar(loader, spawn.JobID);
		}
		finally
		{
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
