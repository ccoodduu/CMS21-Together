using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Player;

namespace CMS21Together.Network.Handlers;

public static class PlayerHandlers
{
	[PacketHandler(PacketTypes.Movement)]
	public static void OnMovementUpdate(long senderId, MovementPacket packet)
	{
		PresenceManager.ApplyMovement(packet);
	}

	[PacketHandler(PacketTypes.PlayerPresence)]
	public static void OnPlayerPresence(long senderId, PlayerPresencePacket packet)
	{
		PresenceManager.ApplyRecord(packet.Record);
	}

	[PacketHandler(PacketTypes.PlayerRoster)]
	public static void OnPlayerRoster(long senderId, PlayerRosterPacket packet)
	{
		PresenceManager.ApplyRoster(packet, SyncTracker.ReceivingSnapshotId);
	}

	[PacketHandler(PacketTypes.PlayerRestore)]
	public static void OnPlayerRestore(long senderId, PlayerRestorePacket packet)
	{
		SpawnPlacement.QueueRestore(packet, SyncTracker.ReceivingSnapshotId);
	}
}
