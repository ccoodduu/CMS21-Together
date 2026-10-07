using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using CMS21_Together_Core.Data.Compatibility;

namespace CMS21_Together_Core.Network.Packets;


[Serializable]
[NetworkPacket(PacketTypes.Heartbeat)]
public class HeartbeatPacket : INetworkData
{
	[OptionalField] public long sentTicks;
}


[Serializable]
[NetworkPacket(PacketTypes.Connect)]
public class ConnectPacket : INetworkData
{
	public int playerID;
	public string username;
	
	public string message;
	public string gameVersion;
	public string modVersion;

	[OptionalField] public string protocolHash;
	[OptionalField] public List<string> dlc;
	[OptionalField] public List<ModReport> mods;
	[OptionalField] public string password;
	[OptionalField] public string adminKey;
	[OptionalField] public string playerKey;
}

[Serializable]
[NetworkPacket(PacketTypes.Disconnect)]
public class DisconnectPacket : INetworkData
{
	public int playerID;
	public string message;
	public DisconnectReason reason;
}
[Serializable]
public enum DisconnectReason
{
	None,
	ServerShutdown,
	Kicked,
	VersionMismatch,
	DuplicateIdentity,
	MissingIdentity,
	SyncFailed,
	ServerFull,
	WrongPassword,
	GameVersionMismatch,
	ModMismatch
}

[Serializable]
[NetworkPacket(PacketTypes.ServerInfo)]
public class ServerInfoPacket : INetworkData
{
	public string ServerName;
	public string ModVersion;
	public int Port;
	public int MaxPlayers;
	public ulong SteamId;
	public string PublicAddress;
	public Data.Enum.Gamemode Difficulty;
	[OptionalField] public List<string> SharedDlc;
	[OptionalField] public bool PasswordRequired;
	[OptionalField] public bool IsAdmin;
	[OptionalField] public string LockScope;
	[OptionalField] public int LockExpirySeconds;
}
