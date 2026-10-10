using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using UnityEngine;

namespace CMS21Together.Logic.Car.Parts;

// The blocked counters are local game state that no digest covers. Each settled car remembers which parts' counters
// equal the number of mounted parts that block them (right after a load a few do not); a watched part whose counter
// differs from that number at two checks in a row has drifted.
public static class PartBlockCheck
{
	private const float IntervalSeconds = 10f;

	private sealed class Watch
	{
		public PartRegistry Registry;
		public int SpawnSeq;
		public readonly List<(PartScript Blocker, string Key)> Pairs = new List<(PartScript, string)>();
		public readonly Dictionary<string, PartScript> Watched = new Dictionary<string, PartScript>();
		public readonly HashSet<string> Suspect = new HashSet<string>();
		public readonly HashSet<string> Reported = new HashSet<string>();
	}

	private static readonly Dictionary<int, Watch> watches = new Dictionary<int, Watch>();
	private static float nextCheck;

	public static int Drifts { get; private set; }

	public static void Reset()
	{
		watches.Clear();
		Drifts = 0;
	}

	public static void Update()
	{
		float now = Time.realtimeSinceStartup;
		if (now < nextCheck || !ClientScene.IsGarageReady) return;
		nextCheck = now + IntervalSeconds;
		foreach (var sync in CarPartsSync.All.ToList())
		{
			if (sync.State != LoaderSyncState.Ready || sync.Registry == null) continue;
			if (PartTransactions.HasOpen(sync.Loader) || Locks.LockLifecycle.All.Any(t => t.Loader == sync.Loader)) continue;
			if (!watches.TryGetValue(sync.Loader, out var watch) || watch.Registry != sync.Registry || watch.SpawnSeq != sync.SpawnSeq)
			{
				watches[sync.Loader] = Begin(sync);
				continue;
			}
			Check(sync.Loader, watch);
		}
	}

	private static Watch Begin(LoaderSync sync)
	{
		var watch = new Watch { Registry = sync.Registry, SpawnSeq = sync.SpawnSeq };
		var parts = new Dictionary<string, PartScript>();
		foreach (string key in sync.Registry.SubKeys)
		{
			var blocker = sync.Registry.Sub(key);
			if (blocker == null) continue;
			foreach (var part in PartBlocking.BlockedBy(blocker))
			{
				if (!sync.Registry.TryGetSubPath(part, out var path)) continue;
				string partKey = PartKeys.Sub(path);
				watch.Pairs.Add((blocker, partKey));
				parts[partKey] = part;
			}
		}
		var expected = Expected(watch);
		foreach (var pair in parts)
			if (pair.Value.blockedNo == (expected.TryGetValue(pair.Key, out int n) ? n : 0)) watch.Watched[pair.Key] = pair.Value;
		return watch;
	}

	private static Dictionary<string, int> Expected(Watch watch)
	{
		var counts = new Dictionary<string, int>();
		foreach (var (blocker, key) in watch.Pairs)
			if (blocker != null && !blocker.IsUnmounted) counts[key] = (counts.TryGetValue(key, out int n) ? n : 0) + 1;
		return counts;
	}

	private static void Check(int loader, Watch watch)
	{
		var expected = Expected(watch);
		foreach (var pair in watch.Watched)
		{
			if (pair.Value == null || watch.Reported.Contains(pair.Key)) continue;
			int want = expected.TryGetValue(pair.Key, out int n) ? n : 0;
			if (pair.Value.blockedNo == want)
			{
				watch.Suspect.Remove(pair.Key);
				continue;
			}
			if (watch.Suspect.Add(pair.Key)) continue;
			watch.Reported.Add(pair.Key);
			Drifts++;
			Log.Error($"[Parts] Loader {loader}: the blocked counter of {pair.Key} ({pair.Value.id}) is {pair.Value.blockedNo}, but {want} mounted parts block it.");
		}
	}
}
