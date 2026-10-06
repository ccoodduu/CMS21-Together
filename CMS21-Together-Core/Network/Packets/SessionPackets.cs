using System;
using System.Collections.Generic;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.PlayerPings)]
public class PlayerPingsPacket : INetworkData
{
	public Dictionary<int, int> Ms = new Dictionary<int, int>();
}

[Serializable]
[NetworkPacket(PacketTypes.KickRequest)]
public class KickRequestPacket : INetworkData
{
	public int PlayerId;
}
