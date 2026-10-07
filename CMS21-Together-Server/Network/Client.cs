using System;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network.Transport;
using Steamworks.Data;

namespace CMS21_Together_Server.Network
{
	public class Client
	{
		private const float RttSmoothing = 0.3f;

		public int ID;
		public long SteamID { get; set; }
		public string Identity { get; set; }
		public bool RestoreOffered { get; set; }

		public NetworkType ConnectionType;

		public Tcp Tcp;
		public Udp Udp;

		public Connection SteamConnection;

		public bool IsConnected;
		public SyncState SyncState = SyncState.Connected;
		public int SnapshotId;
		public long SnapshotRequestedAt;
		public long SnapshotBytes;
		public double SnapshotBuildMs;
		public string SnapshotItems;
		public Action OnConnectedSuccessfully;

		public bool IsAdmin { get; set; }
		public float RttMs { get; private set; }

		public float LastHeartbeatTime { get; set; }
		private float lastHeartbeatTime;
		private bool ConnectionValid;

		public bool IsAccepted => ConnectionValid;

		public Client(int clientId)
		{
			ID = clientId;
			// Tcp/Udp are always allocated: a slot may be claimed by either a
			// DirectIP or Steam connection, and ConnectionType isn't known yet here.
			Tcp = new Tcp(ID);
			Udp = new Udp(ID);
			OnConnectedSuccessfully += OnConnected;
		}

		private void OnConnected()
		{
			float currentTime = ServerTime.Time;

			lastHeartbeatTime = currentTime;
			LastHeartbeatTime = currentTime;
			ConnectionValid = true;
			Logger.Debug($"Client[{ID}] connected successfully!");
			Server.SendToClient(new HeartbeatPacket { sentTicks = ServerTime.Ticks }, ID);
		}

		public void OnHeartbeatEcho(long sentTicks)
		{
			LastHeartbeatTime = ServerTime.Time;
			if (sentTicks <= 0) return;

			float sample = (float)ServerTime.TicksToMs(ServerTime.Ticks - sentTicks);
			if (sample < 0) return;
			RttMs = RttMs <= 0 ? sample : RttSmoothing * sample + (1 - RttSmoothing) * RttMs;
		}

		public void Update()
		{
			if (!ConnectionValid) return;

			if (ServerTime.Time - lastHeartbeatTime >= 3)
			{
				lastHeartbeatTime = ServerTime.Time;
				Server.SendToClient(new HeartbeatPacket { sentTicks = ServerTime.Ticks }, ID, false);
			}

			if (ServerTime.Time - LastHeartbeatTime > Program.CONNECTION_TIMEOUT)
			{
				// Log in English
				Logger.Warn($"Client[{ID}] timed out (No response for {Program.CONNECTION_TIMEOUT}s).");
				Disconnect();
			}
		}

		public void Disconnect(DisconnectReason reason = DisconnectReason.None)
		{
			lock (GameDataManager.StateLock)
			{
				Logger.Debug($"Client {ID} disconnected.");
				Tcp.Disconnect();
				Udp?.Disconnect();
				IsConnected = false;
				ConnectionValid = false;
				SyncState = SyncState.Connected;
				SnapshotId = 0;
				lastHeartbeatTime = 0;
				LastHeartbeatTime = 0;
				IsAdmin = false;
				RttMs = 0;

				PlayerRecords.OnLeft(this);
				Identity = null;
				SteamID = 0;
				RestoreOffered = false;

				if (PresenceRegistry.Remove(ID))
				{
					Logger.Info(reason == DisconnectReason.None ? $"Player {ID} left" : $"Player {ID} left ({reason})");
					Server.SendToClients(new DisconnectPacket()
					{
						playerID = ID,
						message = "Disconnected",
						reason = reason
					}, ID);
					PresenceEvents.RaiseLeft(ID);
				}
				GameDataManager.RequestSave();
			}
		}
	}

	public enum SyncState
	{
		Connected,
		Syncing,
		InSession
	}
}
