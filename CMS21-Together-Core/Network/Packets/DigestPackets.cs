using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.Digest;

namespace CMS21_Together_Core.Network.Packets
{
    public enum DigestTrigger
    {
        Poll,
        ManualResync
    }

    [Serializable]
    public class DigestEntry
    {
        public string Key;
        public string SubKey = "";
        public bool NotReady;
        public ulong Hash;
    }

    [Serializable]
    public class DigestRequestEntry
    {
        public string Key;
        public string SubKey = "";
    }

    [Serializable]
    [NetworkPacket(PacketTypes.StateDigestRequest)]
    public class StateDigestRequestPacket : INetworkData
    {
        public int Seq;
        public List<DigestRequestEntry> Entries = new List<DigestRequestEntry>();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.StateDigest)]
    public class StateDigestPacket : INetworkData
    {
        public int Seq;
        public DigestTrigger Trigger;
        public List<DigestEntry> Entries = new List<DigestEntry>();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.StateDetailRequest)]
    public class StateDetailRequestPacket : INetworkData
    {
        public string Key;
        public string SubKey = "";
    }

    [Serializable]
    [NetworkPacket(PacketTypes.StateDetail)]
    public class StateDetailPacket : INetworkData
    {
        public string Key;
        public string SubKey = "";
        public Projection Projection;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.DesyncNotice)]
    public class DesyncNoticePacket : INetworkData
    {
        public string Key;
        public string SubKey = "";
        public bool Persistent;
    }
}
