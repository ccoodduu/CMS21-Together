using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Tools;

namespace CMS21Together.Network.Handlers;

public static class ToolHandlers
{
	[PacketHandler(PacketTypes.ToolsState)]
	public static void OnState(long clientId, ToolsStatePacket packet)
	{
		int snapshotId = SyncTracker.ReceivingSnapshotId;
		ClientScene.GarageBound(() => ToolSync.OnState(packet, snapshotId),
			() =>
			{
				ToolSync.ReplaceMirror(packet);
				SyncTracker.Applied(SyncOrder.WorkshopToolsKey, snapshotId);
			});
	}

	[PacketHandler(PacketTypes.ToolSlotUpdate)]
	public static void OnSlotUpdate(long clientId, ToolSlotUpdatePacket packet) =>
		ClientScene.GarageBound(() => ToolSync.OnRemoteSlot(packet.State), () => ToolSync.MirrorSlot(packet.State));

	[PacketHandler(PacketTypes.ToolSlotRejected)]
	public static void OnRejected(long clientId, ToolSlotRejectedPacket packet) =>
		ClientScene.GarageBound(() => ToolSync.OnRejected(packet), () => ToolSync.MirrorSlot(packet.Current));

	[PacketHandler(PacketTypes.ToolSlotProperty)]
	public static void OnProperty(long clientId, ToolSlotPropertyPacket packet) =>
		ClientScene.GarageBound(() => ToolSync.OnRemoteProperty(packet), () => ToolSync.MirrorProperty(packet));

	[PacketHandler(PacketTypes.ToolPosition)]
	public static void OnPosition(long clientId, ToolPositionPacket packet) =>
		ClientScene.GarageBound(() => ToolPositionSync.OnRemote(packet), () => ToolSync.MirrorPosition(packet));

	[PacketHandler(PacketTypes.ToolPartChange)]
	public static void OnPartChange(long clientId, ToolPartChangePacket packet) => ClientScene.GarageBound(() => EngineStandParts.OnRemoteChange(packet),
		() =>
		{
			Logic.Car.Parts.PartChanges.ApplyInventory(packet.Delta);
			foreach (var record in packet.SubParts) ToolSync.Mirror(packet.Tool).Parts[record.Key] = record;
		});

	[PacketHandler(PacketTypes.ToolPartChangeResult)]
	public static void OnPartChangeResult(long clientId, ToolPartChangeResultPacket packet) => ClientScene.GarageBound(() => EngineStandParts.OnResult(packet));

	[PacketHandler(PacketTypes.ToolClaimUpdate)]
	public static void OnClaimUpdate(long clientId, ToolClaimUpdatePacket packet) => ToolSync.OnClaimUpdate(packet);
}
