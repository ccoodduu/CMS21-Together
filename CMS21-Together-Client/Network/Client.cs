using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network;
using CMS21Together.Data;
using CMS21Together.Managers;
using CMS21Together.Network.Transport;
using CMS21Together.Session;
using UnityEngine;

namespace CMS21Together.Network;

public class Client
{
	public static Client Instance;
	public ClientTCP Tcp;
	public ClientUDP UDP;
	public ClientSteam Steam;
	public int ID;

	public NetworkType NetworkType;

	public bool IsConnected { get; private set; }
	public bool IsConnectionValid;
	public Action OnConnectionValidated;
	
	public static void Init()
	{
		Instance = new Client();
		Instance.IsConnected = false;
		Instance.Tcp = new ClientTCP();
		Instance.UDP = new ClientUDP();
		Instance.Steam = null;
	}

	public void ConnectToServer(string address = "127.0.0.1")
	{
		if (IsConnected) return;

		var (host, port) = ParseAddress(address);
		NetworkType = NetworkType.DirectIP;
		UDP.endPoint = new IPEndPoint(ResolveHost(host), port);
		Tcp.Connect(host, port);
		Application.runInBackground = true;
		OnConnectionValidated += OnConnectionSuccessful;
		IsConnected = true;
	}

	private static (string Host, int Port) ParseAddress(string address)
	{
		address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
		int colon = address.LastIndexOf(':');
		if (colon > 0 && address.IndexOf(':') == colon && int.TryParse(address.Substring(colon + 1), out int port))
			return (address.Substring(0, colon), port);
		return (address, MainMod.PORT);
	}

	private static IPAddress ResolveHost(string host)
	{
		if (IPAddress.TryParse(host, out IPAddress ip)) return ip;
		return Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
	}

	public void ConnectToSteamServer(ulong serverId)
	{
		if (IsConnected) return;
		
		NetworkType = NetworkType.Steam;
		IsConnected = true;
		Steam = ClientSteam.ConnectToServer(serverId);
		OnConnectionValidated += OnConnectionSuccessful;
		Application.runInBackground = true;
	}
        
	public void Send<T>(T packetData, bool reliable=true) where T : INetworkData
	{
		PacketTypes id = PacketRouter.GetPacketId(packetData);
		using (Packet packet = new Packet((int)id))
		{
			packet.Write(packetData);
			packet.WriteLength();
			if (NetworkType == NetworkType.DirectIP)
			{
				if (reliable)
					Tcp.SendData(packet);
				else
					UDP.SendData(packet);
			}
			else
				Steam.Send(packet, reliable);
		}
	}
	
	private void OnConnectionSuccessful()
	{
		IsConnectionValid = true;
		ConnectionStatus.Set(JoinStatus.Loading);
		
		ModGameManager.LoadPlayerPrefab();
		ModGameManager.StartGame();
	}

	public void Disconnect()
	{
		if (Tcp.socket != null)
			Tcp.Disconnect();
		if (Steam != null && Steam.Connected)
			Steam.Close();
		Application.runInBackground = false;
		IsConnected = false;
		IsConnectionValid = false;
		OnConnectionValidated -= OnConnectionSuccessful;
		ConnectionStatus.OnLocalDisconnect();
		ClientData.ServerInfo = null;
		ClientData.Reset();
		Log.Info("Disconnected from server.");
	}
}