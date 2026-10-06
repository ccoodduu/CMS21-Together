using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Network;
using CMS21Together.Session;
using UnityEngine;

namespace CMS21Together.Data;

public static class SyncTracker
{
	public const float NoProgressTimeout = 30f;
	public const int NoSnapshot = -1;

	private static readonly Dictionary<string, int> applied = new Dictionary<string, int>();
	private static Dictionary<string, int> expected;

	public static int CurrentSnapshotId { get; private set; } = NoSnapshot;
	public static bool InSnapshot { get; private set; }
	public static bool Acked { get; private set; }
	public static float LastProgressTime { get; private set; }

	public static int ReceivingSnapshotId => InSnapshot ? CurrentSnapshotId : NoSnapshot;

	public static void Reset()
	{
		applied.Clear();
		expected = null;
		CurrentSnapshotId = NoSnapshot;
		InSnapshot = false;
		Acked = false;
		MarkProgress();
	}

	public static void MarkProgress()
	{
		LastProgressTime = Time.realtimeSinceStartup;
	}

	public static bool HasTimedOut => !Acked && Time.realtimeSinceStartup - LastProgressTime > NoProgressTimeout;

	public static void OnSyncBegin(SyncBegin packet)
	{
		Reset();
		CurrentSnapshotId = packet.snapshotId;
		InSnapshot = true;
		ConnectionStatus.Set(JoinStatus.Syncing);
		Log.Debug($"[SyncTracker] Snapshot {packet.snapshotId} started.");
	}

	public static void OnSyncEnd(SyncEnd packet)
	{
		if (packet.snapshotId != CurrentSnapshotId)
		{
			Log.Warn($"[SyncTracker] Ignoring SyncEnd of snapshot {packet.snapshotId}, current is {CurrentSnapshotId}.");
			return;
		}

		InSnapshot = false;
		expected = packet.Items ?? new Dictionary<string, int>();
		MarkProgress();
		Log.Debug($"[SyncTracker] Snapshot {packet.snapshotId} ended, expecting {Describe(expected)}.");
		TryAck();
	}

	public static void Applied(string key, int snapshotId)
	{
		if (snapshotId == NoSnapshot || snapshotId != CurrentSnapshotId || Acked) return;

		applied.TryGetValue(key, out int count);
		applied[key] = count + 1;
		MarkProgress();
		TryAck();
	}

	public static string DescribeProgress()
	{
		if (expected == null) return $"snapshot {CurrentSnapshotId}: SyncEnd not received, applied {Describe(applied)}";
		return $"snapshot {CurrentSnapshotId}: " + string.Join(", ", expected.Select(e => $"{e.Key} {Count(applied, e.Key)}/{e.Value}"));
	}

	private static void TryAck()
	{
		if (Acked || expected == null) return;
		if (expected.Any(e => Count(applied, e.Key) < e.Value)) return;

		Acked = true;
		Client.Instance.Send(new SyncAck { snapshotId = CurrentSnapshotId });
		ClientData.IsInitialSyncFinished = true;
		ClientScene.DrainPending();
		ConnectionStatus.Set(JoinStatus.InSession);
		JoinService.OnInSession();
		Log.Success($"Initial synchronization finished (snapshot {CurrentSnapshotId}).");
	}

	private static int Count(Dictionary<string, int> counts, string key) => counts.TryGetValue(key, out int value) ? value : 0;

	private static string Describe(Dictionary<string, int> counts) => string.Join(", ", counts.Select(c => $"{c.Key}={c.Value}"));
}
