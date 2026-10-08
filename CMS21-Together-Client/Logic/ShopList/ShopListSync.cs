using System;
using System.Collections.Generic;
using System.Linq;
using CMS.Containers;
using CMS.UI.Windows;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using UnityEngine;
using GameList = Il2CppSystem.Collections.Generic.List<CMS.Containers.ShopListItemData>;

namespace CMS21Together.Logic.ShopList;

public static class ShopListSync
{
	private const float PollSeconds = 0.1f;
	private const float RetrySeconds = 5f;

	private static List<ShopListEntry> mirror;
	private static bool mirrorPending;
	private static GameList knownList;
	private static int knownVersion;
	private static List<ShopListEntry> known = new List<ShopListEntry>();
	private static int nextSeq = 1;
	private static int lastSentSeq;
	private static int lastAckedSeq;
	private static int applyDepth;
	private static float nextPoll;

	public static bool IsApplying => applyDepth > 0;
	public static IReadOnlyList<ShopListEntry> Mirror => mirror;
	public static bool HasOutstanding => lastAckedSeq < lastSentSeq;

	public static void Reset()
	{
		mirror = null;
		mirrorPending = false;
		knownList = null;
		known = new List<ShopListEntry>();
		lastSentSeq = 0;
		lastAckedSeq = 0;
	}

	public static void OnState(ShopListStatePacket packet)
	{
		bool own = Client.Instance != null && packet.SourcePlayer == Client.Instance.ID;
		if (own && packet.SourceSeq > lastAckedSeq) lastAckedSeq = packet.SourceSeq;
		if (own && !string.IsNullOrEmpty(packet.Refused))
			Log.Warn($"[ShopList] The server did not take all of change {packet.SourceSeq} ({packet.Refused}); the list follows the server.");
		mirror = packet.Entries ?? new List<ShopListEntry>();
		mirrorPending = true;
		Update(true);
	}

	public static void Update(bool now = false)
	{
		if (mirror == null || IsApplying) return;
		float time = Time.realtimeSinceStartup;
		if (!now && time < nextPoll) return;
		nextPoll = time + PollSeconds;

		var window = Window();
		if (window == null) return;
		var list = window.items;
		bool sameList = list != null && knownList != null && list.Pointer == knownList.Pointer;
		if (sameList && list._version != knownVersion && SyncTracker.Acked) Capture(list);
		if (!sameList || (mirrorPending && !HasOutstanding)) Apply(window, list);
	}

	public static List<ShopListEntry> ReadGame() => Read(Window()?.items);

	private static ShopListWindow Window()
	{
		var ui = UIManager.Get();
		if (ui == null) return null;
		var window = ui.ShopListWindow;
		return window == null ? null : window;
	}

	private static void Capture(GameList list)
	{
		var current = Read(list);
		knownVersion = list._version;
		var removed = known.Where(k => !current.Any(c => c.SameItem(k))).ToList();
		var deltas = new List<ShopListEntry>();
		foreach (var entry in current)
		{
			int before = known.FirstOrDefault(k => k.SameItem(entry))?.Amount ?? 0;
			if (entry.Amount != before) deltas.Add(entry.WithAmount(entry.Amount - before));
		}
		known = current;
		if (removed.Count == 0 && deltas.Count == 0) return;

		int seq = nextSeq++;
		lastSentSeq = seq;
		Client.Instance.Send(new ShopListChangePacket { ClientSeq = seq, Removed = removed, Deltas = deltas });
		Log.Info($"[ShopList] Change {seq} sent: {string.Join(", ", removed.Select(r => $"-{r.Describe()}").Concat(deltas.Select(d => $"{(d.Amount > 0 ? "+" : "")}{d.Describe()}")))}.");
	}

	private static void Apply(ShopListWindow window, GameList list)
	{
		var current = Read(list);
		if (list != null && ShopListEntry.SameLists(current, mirror))
		{
			Adopt(list, current);
			return;
		}

		applyDepth++;
		try
		{
			var replacement = new GameList();
			foreach (var entry in mirror)
			{
				var extra = new ShopListItemDataEx();
				if (entry.LicensePlateName != null) extra.LicensePlateName = entry.LicensePlateName;
				extra.LicensePlate = entry.LicensePlate;
				extra.Tire = entry.Tire;
				extra.Rim = entry.Rim;
				extra.Width = entry.Width;
				extra.Size = entry.Size;
				extra.Profile = entry.Profile;
				extra.ET = entry.ET;
				var item = new ShopListItemData { ID = entry.Id, Amount = entry.Amount, AdditionalData = extra };
				replacement.Add(item);
				// The AdditionalData setter copies the struct without a GC write barrier; extra keeps LicensePlateName
				// alive until List.Add has stored it with one.
				GC.KeepAlive(extra);
			}
			window.items = replacement;
			Adopt(replacement, mirror.Select(e => e.WithAmount(e.Amount)).ToList());
			if (window.IsActive) window.FillItems();
			Log.Info($"[ShopList] Shared list applied ({mirror.Count} entries).");
		}
		catch (Exception e)
		{
			Log.Error($"[ShopList] Applying the shared list failed, retrying in {RetrySeconds} s: {e}");
			nextPoll = Time.realtimeSinceStartup + RetrySeconds;
		}
		finally
		{
			applyDepth--;
		}
	}

	private static void Adopt(GameList list, List<ShopListEntry> entries)
	{
		knownList = list;
		knownVersion = list._version;
		known = entries;
		mirrorPending = false;
	}

	private static List<ShopListEntry> Read(GameList list)
	{
		var entries = new List<ShopListEntry>();
		if (list == null) return entries;
		for (int i = 0; i < list.Count; i++)
		{
			var item = list[i];
			var extra = item.AdditionalData;
			entries.Add(new ShopListEntry
			{
				Id = item.ID,
				Amount = item.Amount,
				LicensePlateName = extra.LicensePlateName,
				LicensePlate = extra.LicensePlate,
				Tire = extra.Tire,
				Rim = extra.Rim,
				Width = extra.Width,
				Size = extra.Size,
				Profile = extra.Profile,
				ET = extra.ET,
			});
		}
		return entries;
	}
}
