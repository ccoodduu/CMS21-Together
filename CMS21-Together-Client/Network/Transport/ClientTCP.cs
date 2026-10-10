using System;
using System.Net.Sockets;
using CMS21_Together_Core;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network;
using CMS21Together.Managers;
using CMS21Together.Session;

namespace CMS21Together.Network.Transport;

public class ClientTCP
{
	public TcpClient socket;
    private NetworkStream stream;
    private PacketFramer receivedData;
    private byte[] receiveBuffer;

    public void Connect(string ip, int port)
    {
        try
        {
            socket = new TcpClient
            {
                ReceiveBufferSize = 4096,
                SendBufferSize = 4096
            };

            receiveBuffer = new byte[4096];
            receivedData = new PacketFramer();

            socket.BeginConnect(ip, port, ConnectCallback, null);
        }
        catch (Exception ex)
        {
            Log.Error($"Connection Error : {ex.Message}");
        }
    }

    private void ConnectCallback(IAsyncResult result)
    {
        try
        {
            socket.EndConnect(result);

            if (!socket.Connected) return;

            stream = socket.GetStream();
            stream.BeginRead(receiveBuffer, 0, 4096, ReceiveCallback, null);
            
            Log.Success("Connected to TCP server.");
        }
        catch (Exception ex)
        {
            Log.Error($"Error ConnectCallback : {ex.Message}");
            string detail = ex.Message;
            ThreadManager.ExecuteOnMainThread<object>(_ => ConnectionStatus.Fail(JoinFailure.Unreachable, detail), null);
        }
    }

    private void CloseFromRemote()
    {
        if (socket == null) return;
        Disconnect();
        ThreadManager.ExecuteOnMainThread<object>(_ => JoinService.OnTransportClosed(), null);
    }

    private void ReceiveCallback(IAsyncResult result)
    {
        try
        {
            int byteLength = stream.EndRead(result);
            if (byteLength <= 0)
            {
                CloseFromRemote();
                return;
            }
            ServerWatchdog.MarkReceived();

            if (!receivedData.Feed(receiveBuffer, byteLength, OnPacket))
                Log.Warn("A packet length of zero or less from the server; dropped the buffered bytes.");

            stream.BeginRead(receiveBuffer, 0, 4096, ReceiveCallback, null);
        }
        catch
        {
            CloseFromRemote();
        }
    }

    private static void OnPacket(byte[] packetBytes)
    {
        long receivedMs = PacketClock.NowMs;
        ThreadManager.ExecuteOnMainThread<object>((_) =>
        {
            using (Packet packet = new Packet(packetBytes))
            {
                int packetId = packet.ReadInt();
                try
                {
                    object dataObject = packet.Read<object>();
                    PacketClock.Dispatching(receivedMs);
                    PacketRouter.Dispatch((PacketTypes)packetId, dataObject, 0);
                }
                catch (Exception ex)
                {
                    Log.Error($"Error reading packet {packetId}: {ex.Message}");
                }
            }
        }, null);
    }

    public void SendData(Packet packet)
    {
        try
        {
            if (socket != null)
            {
                byte[] buffer = packet.ToArray();
                stream.BeginWrite(buffer, 0, buffer.Length, null, null);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error sending: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        if (socket == null) return;
        socket?.Close();
        stream = null;
        receivedData = null;
        receiveBuffer = null;
        socket = null;
        Log.Info("TCP Connection closed.");
    }
}