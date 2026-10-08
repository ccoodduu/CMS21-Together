using System;
using System.Collections.Generic;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Outdoor
{
	public static class OutdoorNet
	{
		public static Action<int, INetworkData> Send = SendToConnected;
		public static Func<int, string> NameOf = id => PresenceRegistry.Get(id)?.Username ?? $"Player{id}";
		public static Action<int, int> MembershipChanged = PublishInstanceId;
		public static Func<int> Money = () => GameDataManager.CurrentState.WorldState.Money;
		public static Action HistoryChanged = () => GameDataManager.RequestSave();
		public static Func<float> Now = () => ServerTime.Time;

		public static void SendTo(IEnumerable<int> clients, INetworkData packet, int except = -1)
		{
			foreach (int client in clients)
				if (client != except) Send(client, packet);
		}

		private static void SendToConnected(int client, INetworkData packet)
		{
			if (Server.Clients.TryGetValue(client, out var connection) && connection.IsConnected)
				Server.SendToClient(packet, client);
		}

		private static void PublishInstanceId(int playerId, int instanceId)
		{
			var record = PresenceRegistry.Get(playerId);
			if (record == null || record.OutdoorInstanceId == instanceId) return;
			record.OutdoorInstanceId = instanceId;
			Server.SendToClients(new PlayerPresencePacket { Record = record.Copy() }, playerId);
		}
	}
}
