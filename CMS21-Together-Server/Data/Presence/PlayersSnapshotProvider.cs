using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Persistence;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Presence
{
	[SessionSection]
	public class PlayersSnapshotProvider : ISnapshotProvider
	{
		public string Key => SyncOrder.PlayersKey;
		int ISnapshotProvider.SyncOrder => SyncOrder.Players;

		public int SendSnapshot(int clientId)
		{
			var roster = new PlayerRosterPacket
			{
				Records = PresenceRegistry.All.Where(r => r.PlayerId != clientId).Select(r => r.Copy()).ToList()
			};
			Server.SendToClient(roster, clientId);
			return 1;
		}
	}
}
