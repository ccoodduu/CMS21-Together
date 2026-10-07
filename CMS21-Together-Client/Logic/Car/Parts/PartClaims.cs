using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Locks;
using CMS21Together.Network;

namespace CMS21Together.Logic.Car.Parts;

public static class PartClaims
{
	public const int Released = CarLockUpdatePacket.Released;

	public static string LastBlocked { get; internal set; }

	public static void Reset() => LastBlocked = null;

	public static event Action<int, IReadOnlyList<string>, int, bool> ClaimChanged;

	public static IReadOnlyDictionary<string, int> Held(int loader)
	{
		var held = new Dictionary<string, int>();
		foreach (var record in CarLockMirror.All.Where(r => r.Loader == loader))
		foreach (string key in record.X.Where(LockKeys.IsPart))
			held[key] = record.Owner;
		return held;
	}

	public static int OwnerOf(int loader, string key) => Held(loader).TryGetValue(key, out int owner) ? owner : Released;

	public static bool HeldByOther(int loader, IEnumerable<string> keys, out int owner)
	{
		owner = Released;
		var held = Held(loader);
		foreach (string key in keys)
		{
			if (!held.TryGetValue(key, out int holder) || holder == Client.Instance.ID) continue;
			owner = holder;
			return true;
		}
		return false;
	}

	internal static void OnLockChanged(LockRecord record, bool released, bool fromSnapshot)
	{
		var keys = record.X.Where(LockKeys.IsPart).ToList();
		if (keys.Count == 0) return;
		try
		{
			ClaimChanged?.Invoke(record.Loader, keys, released ? Released : record.Owner, fromSnapshot);
		}
		catch (Exception e)
		{
			CMS21_Together_Core.Logging.Log.Error($"[Visuals] Claim visual failed on loader {record.Loader}: {e.Message}");
		}
	}
}
