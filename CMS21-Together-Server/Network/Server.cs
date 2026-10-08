using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.Compatibility;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Diagnostics.Perf;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network.Transport;

namespace CMS21_Together_Server.Network
{
	public static class Server
	{
		public static int MaxPlayers { get; private set; }
        public static int Port { get; private set; }
        public static Dictionary<int, Client> Clients = new Dictionary<int, Client>();
        
        private static TcpListener  tcpListener;
        public static SteamTransport steamTransport { get; private set; }
        private static UdpClient udpListener;
        private static bool isRunning;

        public static void Start(int maxPlayers, int port)
        {
            if (isRunning)
            {
                Logger.Info("Server is already running.");
                return;
            }
            isRunning = true;
            MaxPlayers = maxPlayers;
            Port = port;

            Logger.Debug("Starting TCP socket...");
            TrafficCounters.Initialize(maxPlayers);
            InitializeServerData();

            tcpListener = new TcpListener(IPAddress.Any, Port);
            tcpListener.Start();
            tcpListener.BeginAcceptTcpClient(TcpConnectCallback, null);
            
            udpListener = new UdpClient(Port);
            udpListener.BeginReceive(UDPReceiveCallback, null);

            if (Program.Config.UseSteam)
            {
                steamTransport = SteamTransport.Initialize(Port);
            }
        }

        private static void TcpConnectCallback(IAsyncResult result)
        {
            try
            {
                if (tcpListener == null || !tcpListener.Server.IsBound)
                    return;

                TcpClient client = tcpListener.EndAcceptTcpClient(result);
                tcpListener.BeginAcceptTcpClient(TcpConnectCallback, null);

                Logger.Debug($"Pending connection: {client.Client.RemoteEndPoint}...");

                for (int i = 1; i <= MaxPlayers; i++)
                {
                    if (!Clients[i].IsConnected)
                    {
                        Clients[i].Tcp.Connect(client);
                        Clients[i].IsConnected = true;
                        Clients[i].ConnectionType = NetworkType.DirectIP;

                        SendToClient(WelcomePacket(Clients[i].ID), Clients[i].ID);
                        return;
                    }
                }

                Logger.Info($"Connection refused: server full ({client.Client.RemoteEndPoint}).");
                RefuseUnassigned(client, DisconnectReason.ServerFull, $"The server is full ({MaxPlayers} players).");
            }
            catch (ObjectDisposedException) { return; }
            catch (Exception e)
            {
                Logger.Error($"Error TCPConnect: {e.Message}");
            }
        }
        
        public static ConnectPacket WelcomePacket(int clientId) => new ConnectPacket
        {
            gameVersion = "",
            username = "",
            message = "Welcome to server!",
            modVersion = Program.MOD_VERSION,
            protocolHash = ProtocolHash.Value,
            playerID = clientId
        };

        public static void RefuseUnreadableConnect(int clientId, PacketTypes packetType)
        {
            if (packetType != PacketTypes.Connect || !Clients.TryGetValue(clientId, out var client) || client.IsAccepted) return;
            Refuse(clientId, DisconnectReason.VersionMismatch,
                $"The server ({Program.MOD_VERSION}) could not read your connection details: your build of Together differs from the server's.");
        }

        private static void UDPReceiveCallback(IAsyncResult _result)
        {
            try
            {
                IPEndPoint _clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] _data = udpListener.EndReceive(_result, ref _clientEndPoint);
                
                udpListener.BeginReceive(UDPReceiveCallback, null);
                if (_data.Length < 4) return;

                using (Packet _packet = new Packet(_data))
                {
                    int _clientId = _packet.ReadInt();
                    if (_clientId == 0) return; 

                    if (Clients.TryGetValue(_clientId, out Client client))
                    {
                        if (client.Udp.endPoint == null)
                        {
                            client.Udp.Connect(_clientEndPoint);
                            return;
                        }
                        
                        if (client.Udp.endPoint.ToString() == _clientEndPoint.ToString())
                        {
                            client.Udp.HandleData(_packet);
                        }
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception e)
            {
                Logger.Error($"Error on UDP Receive: {e.Message}");
            }
        }

        private static void InitializeServerData()
        {
            for (int i = 1; i <= MaxPlayers; i++)
            {
                Clients.Add(i, new Client(i));
            }
        }

        public static void Dispatch(int clientId, PacketTypes id, object data)
        {
            long waitStart = Stopwatch.GetTimestamp();
            lock (Data.GameDataManager.StateLock)
            {
                long acquired = Stopwatch.GetTimestamp();
                try
                {
                    if (PacketRouter.RequiresSync(id) && Clients.TryGetValue(clientId, out var client) && client.SyncState != SyncState.InSession)
                    {
                        Logger.Warn($"Client[{clientId}] is not in session ({client.SyncState}): dropped {id}.");
                        return;
                    }
                    PacketRouter.Dispatch(id, data, clientId);
                }
                finally
                {
                    HandlerTimings.Record(HandlerTimings.KeyFor((int)id), acquired - waitStart, Stopwatch.GetTimestamp() - acquired);
                }
            }
        }

        public static void SendToClients<T>(T packetData, int exceptClient=-1, bool reliable = true) where T : INetworkData
        {
            foreach (Client client in Clients.Values)
            {
                if (client.IsConnected && client.SyncState != SyncState.Connected && client.ID != exceptClient)
                {
                    SendToClient(packetData, client.ID, reliable);
                }
            }
        }
        
        public static void SendToClient<T>(T packetData, int clientID, bool reliable=true) where T : INetworkData
        {
            PacketTypes id = PacketRouter.GetPacketId(packetData);
            using (Packet packet = new Packet((int)id))
            {
                packet.Write(packetData);
                packet.WriteLength();
                if (Clients[clientID].ConnectionType == NetworkType.DirectIP)
                {
                    TrafficCounters.CountSent(clientID, (int)id, reliable ? PerfTransport.Tcp : PerfTransport.Udp, packet.Length());
                    if (reliable)
                        Clients[clientID].Tcp.SendData(packet);
                    else
                        Clients[clientID].Udp.SendData(packet);
                }
                else
                {
                    byte[] bytes = packet.ToArray();
                    TrafficCounters.CountSent(clientID, (int)id, PerfTransport.Steam, bytes.Length);
                    steamTransport.SendData(Clients[clientID].SteamConnection, bytes, reliable);
                }
            }
        }
        
        private static readonly Dictionary<int, DisconnectReason> pendingRefusals = new Dictionary<int, DisconnectReason>();

        // The slot is closed on the next tick so the refusal packet goes out first.
        public static void Refuse(int clientId, DisconnectReason reason, string message)
        {
            lock (Data.GameDataManager.StateLock)
            {
                Logger.Info($"Refusing client {clientId}: {reason} ({message})");
                SendToClient(new DisconnectPacket { playerID = clientId, reason = reason, message = message }, clientId);
                if (!pendingRefusals.ContainsKey(clientId)) pendingRefusals[clientId] = reason;
            }
        }

        public static bool Kick(int playerId, string by)
        {
            if (!Clients.TryGetValue(playerId, out var client) || !client.IsConnected)
            {
                Logger.Warn($"Player ID {playerId} not found or not connected.");
                return false;
            }
            Logger.Info($"Kicking player {playerId} ({by})...");
            Refuse(playerId, DisconnectReason.Kicked, "You have been kicked by the server.");
            return true;
        }

        private static void RefuseUnassigned(TcpClient socket, DisconnectReason reason, string message)
        {
            try
            {
                using (Packet packet = new Packet((int)PacketTypes.Disconnect))
                {
                    packet.Write(new DisconnectPacket { playerID = -1, reason = reason, message = message });
                    packet.WriteLength();
                    byte[] bytes = packet.ToArray();
                    TrafficCounters.CountSent(0, (int)PacketTypes.Disconnect, PerfTransport.Tcp, bytes.Length);
                    socket.GetStream().Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"Could not send the refusal: {e.Message}");
            }
            finally
            {
                socket.Close();
            }
        }

        private static void ProcessRefusals()
        {
            if (pendingRefusals.Count == 0) return;
            foreach (var refusal in pendingRefusals.ToArray())
            {
                var client = Clients[refusal.Key];
                if (!client.IsConnected) continue;
                if (client.ConnectionType == NetworkType.Steam) client.SteamConnection.Close();
                client.Disconnect(refusal.Value);
            }
            pendingRefusals.Clear();
        }

        public static void SendUDPData(IPEndPoint _clientEndPoint, Packet _packet)
        {
            try
            {
                if (_clientEndPoint != null)
                {
                    udpListener.BeginSend(_packet.ToArray(), _packet.Length(), _clientEndPoint, null, null);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Error on UDP Send: {e.Message}");
            }
        }

        public static void Stop()
        {
            if (!isRunning) return;
            isRunning = false;
            
            foreach (Client client in Clients.Values)
            {
                if (!client.IsConnected) continue;
                SendToClient(new DisconnectPacket()
                {
                    playerID = -1,
                    message = "Server is closing.",
                    reason = DisconnectReason.ServerShutdown
                }, client.ID);
            }
            
            tcpListener.Stop();
            udpListener?.Close();
            steamTransport?.Shutdown();
            Logger.Info("Server Stopped.");
        }

        private const float StallSeconds = 2f;
        private static float lastUpdateTime = -1f;

        public static void Update()
        {
            if (!isRunning) return;
            if (Program.Config.UseSteam && steamTransport != null)
                steamTransport.Update();
            
            long waitStart = Stopwatch.GetTimestamp();
            lock (Data.GameDataManager.StateLock)
            {
                long acquired = Stopwatch.GetTimestamp();
                try
                {
                    UpdateLocked();
                }
                finally
                {
                    HandlerTimings.Record(HandlerTimings.Tick, acquired - waitStart, Stopwatch.GetTimestamp() - acquired);
                }
            }
        }

        private static void UpdateLocked()
        {
            float now = Data.ServerTime.Time;
            if (lastUpdateTime >= 0f && now - lastUpdateTime > StallSeconds)
            {
                Logger.Warn($"Server loop stalled for {now - lastUpdateTime:0.0} s; heartbeat deadlines moved by that much.");
                foreach (var stalled in Clients.Values) stalled.LastHeartbeatTime += now - lastUpdateTime;
            }
            lastUpdateTime = now;
            ProcessRefusals();
            Data.Cars.CarClaims.Expire(Data.ServerTime.Time);
            Data.Tools.ToolsStore.Expire(Data.ServerTime.Time);
            Data.Reconciliation.ReconciliationService.Tick(Data.ServerTime.Time);
            Data.Jobs.JobsService.Tick(Data.ServerTime.Time);
            Data.Cars.CarDetailsStore.Tick(Data.ServerTime.Time);
            Data.Cars.CarAwayRegistry.Tick(Data.ServerTime.Time);
            Data.Outdoor.OutdoorInstances.Tick(Data.ServerTime.Time);
            foreach (var client in Clients.Values)
            {
                if (client.IsConnected)
                {
                    client.Update();
                }
            }
            BroadcastPings();
        }

        private const float PingInterval = 3f;
        private static float lastPingBroadcast;

        private static void BroadcastPings()
        {
            if (Data.ServerTime.Time - lastPingBroadcast < PingInterval) return;
            lastPingBroadcast = Data.ServerTime.Time;

            var inSession = Clients.Values.Where(c => c.IsConnected && c.SyncState == SyncState.InSession).ToList();
            if (inSession.Count == 0) return;

            var packet = new PlayerPingsPacket();
            foreach (var client in inSession) packet.Ms[client.ID] = (int)Math.Round(client.RttMs);
            foreach (var client in inSession) SendToClient(packet, client.ID, false);
        }
    }
}