using CMS21Together.Logic.Seeding;
using CMS21Together.Network;
using HarmonyLib;

namespace CMS21Together.Logic.Car.Placement;

// A car on a place with a CarPlaceRotation gets a random extra yaw (SetAdditionalCarRot). In a session the roll comes
// from the car, its loader and its place, so every client turns the car alike; a saved angle (LoadCarFromFile) is
// replaced by the same roll.
[HarmonyPatch]
public static class CarAngleSeed
{
	private static bool rolling;

	private static bool Connected => Client.Instance != null && Client.Instance.IsConnectionValid;

	private static bool TrySeed(CarLoader carLoader, out int seed)
	{
		seed = 0;
		if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) return false;
		var ground = carLoader.groundPosition;
		if (ground == null || ground.GetComponent<CarPlaceRotation>() == null) return false;
		var places = CarLoaderPlaces.Get();
		if (places == null) return false;
		int loader = places.GetCarLoaderId(carLoader);
		if (loader < 0) return false;
		int place = (int)places.TransformToCarPlace(ground);
		seed = SeededStreams.StreamSeed(place, "car-angle:" + carLoader.carToLoad, loader);
		return true;
	}

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.SetAdditionalCarRot))]
	[HarmonyPrefix]
	private static bool BeforeSetAdditionalCarRot(CarLoader __instance)
	{
		if (rolling || !Connected || !TrySeed(__instance, out int seed)) return true;
		rolling = true;
		try
		{
			SeededStreams.WithSeed(seed, () =>
			{
				__instance.SetAdditionalCarRot(true, 0f);
				return 0;
			});
		}
		finally
		{
			rolling = false;
		}
		return false;
	}
}
