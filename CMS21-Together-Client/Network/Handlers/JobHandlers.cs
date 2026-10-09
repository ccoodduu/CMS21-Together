using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Jobs;

namespace CMS21Together.Network.Handlers;

public static class JobHandlers
{
	[PacketHandler(PacketTypes.JobsState)]
	public static void OnJobsState(long clientId, JobsStatePacket packet)
	{
		int snapshotId = SyncTracker.ReceivingSnapshotId;
		JobsSync.OnState(packet, snapshotId);
	}

	[PacketHandler(PacketTypes.OrderGeneratorRole)]
	public static void OnRole(long clientId, OrderGeneratorRolePacket packet) => JobsSync.OnRole(packet);

	[PacketHandler(PacketTypes.OrderRequest)]
	public static void OnOrderRequest(long clientId, OrderRequestPacket packet) => JobsSync.OnOrderRequest(packet);

	[PacketHandler(PacketTypes.OrderAdded)]
	public static void OnOrderAdded(long clientId, OrderAddedPacket packet) => JobsSync.OnOrderAdded(packet);

	[PacketHandler(PacketTypes.OrderActionResult)]
	public static void OnActionResult(long clientId, OrderActionResultPacket packet) => ClientScene.GarageBound(() => JobsSync.OnActionResult(packet));

	[PacketHandler(PacketTypes.JobStarted)]
	public static void OnJobStarted(long clientId, JobStartedPacket packet) => JobsSync.OnJobStarted(packet);

	[PacketHandler(PacketTypes.JobRemoved)]
	public static void OnJobRemoved(long clientId, JobRemovedPacket packet) => JobsSync.OnJobRemoved(packet);

	[PacketHandler(PacketTypes.JobStatsAward)]
	public static void OnJobStatsAward(long clientId, JobStatsAwardPacket packet) => JobStats.OnAward(packet);
}
