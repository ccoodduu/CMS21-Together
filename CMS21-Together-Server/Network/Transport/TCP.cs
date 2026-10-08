using System;
using System.Net.Sockets;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Server.Diagnostics.Perf;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Transport
{
	public class Tcp
	{
		public TcpClient Socket;
        private readonly int id;
        private NetworkStream stream;
        private PacketFramer receivedData;
        private byte[] receiveBuffer;
        

        public Tcp(int id)
        {
            this.id = id;
        }

        public void Connect(TcpClient socket)
        {
            Socket = socket;
            Socket.ReceiveBufferSize = 4096;
            Socket.SendBufferSize = 4096;

            stream = Socket.GetStream();
            receivedData = new PacketFramer();
            receiveBuffer = new byte[4096];

            stream.BeginRead(receiveBuffer, 0, 4096, ReceiveCallback, null);
        }

        private void ReceiveCallback(IAsyncResult result)
        {
            try
            {
                int byteLength = stream.EndRead(result);
                if (byteLength <= 0)
                {
                    Server.Clients[id].Disconnect();
                    return;
                }

                if (!receivedData.Feed(receiveBuffer, byteLength, OnPacket))
                    Logger.Warn($"[Network] Client {id}: a packet length of zero or less; dropped the buffered bytes.");

                stream.BeginRead(receiveBuffer, 0, 4096, ReceiveCallback, null);
            }
            catch (Exception)
            {
                Server.Clients[id].Disconnect();
            }
        }

        private void OnPacket(byte[] packetBytes)
        {
            using (Packet packet = new Packet(packetBytes))
            {
                int packetId = packet.ReadInt();
                TrafficCounters.CountReceived(id, packetId, PerfTransport.Tcp, packetBytes.Length + 4);

                try
                {
                    object packetData = packet.Read<object>();
                    Server.Dispatch(id, (PacketTypes)packetId, packetData);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error packet {packetId}: {ex.Message}");
                    Server.RefuseUnreadableConnect(id, (PacketTypes)packetId);
                }
            }
        }

        public void SendData(Packet packet)
        {
            try
            {
                if (Socket != null)
                {
                    stream.BeginWrite(packet.ToArray(), 0, packet.Length(), null, null);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Erreur sending TCP data to {id} : {e.Message}");
            }
        }

        public void Disconnect()
        {
            Socket?.Close();
            stream = null;
            receivedData = null;
            receiveBuffer = null;
            Socket = null;
        }
	}
}