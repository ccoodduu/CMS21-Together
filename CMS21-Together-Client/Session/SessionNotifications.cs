using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Player;
using CMS21Together.UI;

namespace CMS21Together.Session;

public static class SessionNotifications
{
	private static bool rosterSeen;

	public static void Initialize()
	{
		PresenceManager.PlayerAdded += OnPlayerAdded;
		PresenceManager.PlayerRemoved += OnPlayerRemoved;
		PresenceManager.RosterApplied += OnRosterApplied;
	}

	public static void Reset() => rosterSeen = false;

	private static void OnPlayerAdded(PlayerPresenceRecord record, bool fromSnapshot)
	{
		if (fromSnapshot || !rosterSeen) return;
		ModNotify.ShowToast($"{record.Username} joined");
	}

	private static void OnPlayerRemoved(PlayerPresenceRecord record, DisconnectReason reason)
	{
		string name = record?.Username ?? "A player";
		ModNotify.ShowToast(reason == DisconnectReason.Kicked ? $"{name} was kicked" : $"{name} left");
	}

	private static void OnRosterApplied()
	{
		if (rosterSeen) return;
		rosterSeen = true;
		int others = PresenceManager.Roster.Count;
		if (others > 0) ModNotify.ShowToast(others == 1 ? "1 other player online" : $"{others} other players online");
	}
}
