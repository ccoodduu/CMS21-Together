using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data;

namespace CMS21_Together_Core.Network.Packets
{
    [Serializable]
    [NetworkPacket(PacketTypes.ShopListChange)]
    public class ShopListChangePacket : INetworkData
    {
        public int ClientSeq;
        public List<ShopListEntry> Removed = new List<ShopListEntry>();
        public List<ShopListEntry> Deltas = new List<ShopListEntry>();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ShopListState)]
    public class ShopListStatePacket : INetworkData
    {
        public const int FromServer = 0;

        public List<ShopListEntry> Entries = new List<ShopListEntry>();
        public int Revision;
        public int SourcePlayer = FromServer;
        public int SourceSeq;
        public string Refused;
    }
}
