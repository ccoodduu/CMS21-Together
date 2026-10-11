using System;
using System.Collections.Generic;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.StatsAction)]
public class StatsActionPacket : INetworkData
{
    public int ExpDelta;
    public int ScrapsDelta;
}

[Serializable]
public class StatIncrement
{
    public string Id;
    public int Amount;
}

[Serializable]
[NetworkPacket(PacketTypes.EventStatsAward)]
public class EventStatsAwardPacket : INetworkData
{
    public string Event;
    public int Actor;
    public List<StatIncrement> Stats = new List<StatIncrement>();
}
