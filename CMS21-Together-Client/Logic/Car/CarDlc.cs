namespace CMS21Together.Logic.Car;

public static class CarDlc
{
	public const int BaseGame = -1;

	private static CarBundleLoader bundleLoader;

	// CarConfigData.DLC is 1-based (0 = base game), the same offset CarBundleLoader.CheckHaveDLCForCar applies before
	// it calls PlatformManager.IsDLCInstalled with the DLC list position.
	public static int For(string carId)
	{
		if (string.IsNullOrEmpty(carId)) return BaseGame;
		if (bundleLoader == null) bundleLoader = UnityEngine.Object.FindObjectOfType<CarBundleLoader>();
		var cars = bundleLoader?.CarNamesData;
		if (cars == null) return BaseGame;
		for (int i = 0; i < cars.Count; i++)
			if (cars[i] != null && cars[i].CarID == carId) return cars[i].DLC - 1;
		return BaseGame;
	}
}
