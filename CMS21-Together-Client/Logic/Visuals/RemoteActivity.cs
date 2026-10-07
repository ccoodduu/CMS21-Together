using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Player;

namespace CMS21Together.Logic.Visuals;

// remote-visual-feedback D5/D7: the latest activity of each player lives in their roster record (the server's
// players snapshot carries it to a late joiner); WorkPose and ToolProps read it every frame.
public static class RemoteActivity
{
	private static bool subscribed;

	public static int Received { get; private set; }

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		PresenceManager.PlayerRemoved += (record, reason) => VisualScope.CancelPlayer(record.PlayerId, "player left");
	}

	public static void Reset() => Received = 0;

	public static PlayerActivityState Of(int playerId) =>
		PresenceManager.Roster.TryGetValue(playerId, out var player) ? player.Record?.Activity : null;

	public static void OnPacket(PlayerActivityPacket packet)
	{
		if (packet?.State == null || !PresenceManager.Roster.TryGetValue(packet.PlayerId, out var player) || player.Record == null) return;
		Received++;
		player.Record.Activity = packet.State.IsIdle ? null : packet.State;
		Log.Debug($"[Visuals] Player {packet.PlayerId} activity: {packet.State}.");
		BoltReplay.OnActivity(packet.PlayerId, packet.State);
	}
}
