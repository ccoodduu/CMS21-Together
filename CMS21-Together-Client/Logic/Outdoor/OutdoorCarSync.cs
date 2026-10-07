using System;
using System.Collections.Generic;
using System.Linq;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Player;
using CMS21Together.UI;

namespace CMS21Together.Logic.Outdoor;

public static class OutdoorCarSync
{
	private static readonly Dictionary<IntPtr, int> indexByLoader = new Dictionary<IntPtr, int>();
	private static readonly Dictionary<int, CarLoader> loaderByIndex = new Dictionary<int, CarLoader>();
	private static readonly HashSet<int> sold = new HashSet<int>();
	private static readonly HashSet<int> notHere = new HashSet<int>();

	public static IReadOnlyCollection<int> Sold => sold;

	public static IEnumerable<int> CreatedIndices => loaderByIndex.Keys.OrderBy(i => i).ToList();

	public static CarLoader LoaderAt(int index) => loaderByIndex.TryGetValue(index, out var loader) ? loader : null;

	public static void Reset()
	{
		indexByLoader.Clear();
		loaderByIndex.Clear();
		sold.Clear();
		notHere.Clear();
	}

	public static void OnInstance(OutdoorInstancePacket packet)
	{
		foreach (int index in packet.Sold) sold.Add(index);
	}

	public static void ApplyPick(GameScene scene, int index, ref CarsIdWithConfig car)
	{
		if (!OutdoorSession.IsShared || OutdoorSession.Scene != scene) return;
		var picks = OutdoorSession.Instance.Picks;
		if (index < 0 || index >= picks.Count)
		{
			Log.Warn($"[Outdoor] {scene} car {index}: no pick from the server ({picks.Count} picks); the game's own car {car?.CarID} stays.");
			notHere.Add(index);
			return;
		}
		var pick = picks[index];
		var local = CatalogReporter.Find(scene == GameScene.Barn ? OutdoorCatalogScene.Barn : OutdoorCatalogScene.Junkyard, pick.CarId, pick.ConfigVersion);
		if (local == null)
		{
			Log.Warn($"[Outdoor] {scene} car {index}: pick {pick} cannot be loaded here; the game's own car {car?.CarID} stays and cannot be bought.");
			notHere.Add(index);
			return;
		}
		car = local;
	}

	public static void Created(CarLoader loader, int index)
	{
		if (!OutdoorSession.IsShared || loader == null) return;
		indexByLoader[loader.Pointer] = index;
		loaderByIndex[index] = loader;
		if (!sold.Contains(index)) return;
		Log.Info($"[Outdoor] Car {index} ({loader.carToLoad}) was sold before this visit; removed.");
		Delete(index);
	}

	public static int IndexOf(CarLoader loader) =>
		loader != null && OutdoorSession.IsShared && indexByLoader.TryGetValue(loader.Pointer, out int index) ? index : -1;

	public static bool CannotBuy(CarLoader loader, out string why)
	{
		int index = IndexOf(loader);
		why = index < 0 ? null
			: sold.Contains(index) ? "Another player bought this car first."
			: notHere.Contains(index) ? "This car differs from the other players' car and cannot be bought while playing together."
			: null;
		return why != null;
	}

	public static void OnRemoved(OutdoorCarRemovedPacket packet)
	{
		if (!OutdoorSession.IsShared || packet.InstanceId != OutdoorSession.InstanceId) return;
		sold.Add(packet.Index);
		string by = PresenceManager.Roster.TryGetValue(packet.By, out var player) ? player.Record.Username : $"Player {packet.By}";
		Log.Info($"[Outdoor] Car {packet.Index} bought by {by}.");
		if (Delete(packet.Index)) ModNotify.ShowToast($"{by} bought a car here.");
	}

	private static bool Delete(int index)
	{
		if (!loaderByIndex.TryGetValue(index, out var loader) || loader == null) return false;
		try
		{
			var windows = WindowManager.Instance;
			if (windows != null && windows.IsWindowActive(WindowID.CarInfo))
			{
				var info = windows.GetWindowByID<CarInfoWindow>(WindowID.CarInfo);
				if (info != null && info.currentCarLoader != null && info.currentCarLoader.Pointer == loader.Pointer)
					windows.Hide(WindowID.CarInfo, true);
			}
			loader.DeleteCar(true);
			loaderByIndex.Remove(index);
			return true;
		}
		catch (Exception ex)
		{
			Log.Warn($"[Outdoor] Removing car {index} failed: {ex.Message}");
			return false;
		}
	}

	public static List<object> Describe() =>
		loaderByIndex.OrderBy(c => c.Key).Select(c => (object)new
		{
			index = c.Key,
			carToLoad = c.Value == null ? null : c.Value.carToLoad,
			version = c.Value == null ? 0 : c.Value.ConfigVersion,
			sold = sold.Contains(c.Key),
			notHere = notHere.Contains(c.Key),
		}).ToList();
}
