using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car;
using CMS21Together.Network;

namespace CMS21Together.Logic.Outdoor;

public static class CatalogReporter
{
	private static readonly Dictionary<OutdoorCatalogScene, List<CarsIdWithConfig>> local = new Dictionary<OutdoorCatalogScene, List<CarsIdWithConfig>>();
	private static bool sent;

	public static IReadOnlyDictionary<OutdoorCatalogScene, int> Sizes => local.ToDictionary(s => s.Key, s => s.Value.Count);

	public static void Reset()
	{
		sent = false;
		local.Clear();
	}

	public static void OnInSession()
	{
		if (sent || Client.Instance == null || !Client.Instance.IsConnectionValid) return;
		try
		{
			var packet = Build();
			Client.Instance.Send(packet);
			sent = true;
			Log.Info($"[Outdoor] Catalog sent: {string.Join(", ", packet.Scenes.Select(s => $"{s.Key} {s.Value.Count}"))}.");
		}
		catch (Exception ex)
		{
			Log.Warn($"[Outdoor] Reading the car catalog failed: {ex.Message}");
		}
	}

	public static OutdoorCatalogPacket Build()
	{
		var packet = new OutdoorCatalogPacket();
		foreach (OutdoorCatalogScene scene in Enum.GetValues(typeof(OutdoorCatalogScene)))
		{
			var cars = Read(scene);
			packet.Scenes[scene] = cars.Select(c => new CatalogEntry
			{
				CarId = c.CarID, ConfigVersion = c.ConfigVersion, Dlc = CarDlc.For(c.CarID), Rarity = CatalogEntry.UnknownRarity,
			}).ToList();
		}
		return packet;
	}

	public static CarsIdWithConfig Find(OutdoorCatalogScene scene, string carId, int configVersion) =>
		Read(scene).FirstOrDefault(c => c.CarID == carId && c.ConfigVersion == configVersion);

	private static List<CarsIdWithConfig> Read(OutdoorCatalogScene scene)
	{
		if (local.TryGetValue(scene, out var cached)) return cached;
		var loader = Singleton<GameManager>.Instance?.CarBundleLoader;
		if (loader == null) return new List<CarsIdWithConfig>();
		var list = loader.GetCarsForScene(SceneTypeFor(scene));
		var cars = new List<CarsIdWithConfig>();
		for (int i = 0; list != null && i < list.Count; i++)
			if (list[i] != null && !string.IsNullOrEmpty(list[i].CarID)) cars.Add(list[i]);
		local[scene] = cars;
		return cars;
	}

	private static SceneType SceneTypeFor(OutdoorCatalogScene scene)
	{
		switch (scene)
		{
			case OutdoorCatalogScene.Barn: return SceneType.Barn;
			case OutdoorCatalogScene.AuctionNormal: return SceneType.Auction;
			default: return SceneType.Junkyard;
		}
	}
}
