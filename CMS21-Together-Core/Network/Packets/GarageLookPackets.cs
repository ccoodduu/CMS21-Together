using System;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.GarageLookUpdate)]
public class GarageLookUpdatePacket : INetworkData
{
	public ModGarageLook Look;
}

[Serializable]
[NetworkPacket(PacketTypes.GarageLookClaim)]
public class GarageLookClaimPacket : INetworkData
{
	public bool Release;
}

[Serializable]
[NetworkPacket(PacketTypes.GarageLookClaimResult)]
public class GarageLookClaimResultPacket : INetworkData
{
	public const int NoHolder = -1;

	public bool Granted;
	public int HolderPlayerId = NoHolder;
}
