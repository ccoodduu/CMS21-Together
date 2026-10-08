using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets
{
    public enum CarAwayKind
    {
        TestTrack,
        PathTest,
        Dyno
    }

    public enum CarAwayRefusal
    {
        None,
        Busy,
        InUse,
        NotReady,
        Unknown,
        Seated
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarAwayRequest)]
    public class CarAwayRequestPacket : INetworkData
    {
        public int RequestId;
        public int CarLoaderID;
        public int SpawnSeq;
        public CarAwayKind Kind;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarAwayUpdate)]
    public class CarAwayUpdatePacket : INetworkData
    {
        public int CarLoaderID;
        public int SpawnSeq;
        public CarAwayKind Kind;
        public int OwnerPlayerId = -1;
        public int RequestId;
        public CarAwayRefusal Refusal;
        public int HolderPlayerId = -1;
        public int SpecialState = -1;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarAwayRelease)]
    public class CarAwayReleasePacket : INetworkData
    {
        public int CarLoaderID;
        public int SpawnSeq;
        public int SpecialState = -1;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.TestDriveResult)]
    public class TestDriveResultPacket : INetworkData
    {
        public int CarLoaderID;
        public int SpawnSeq;
        public float MileageDeltaKm;
        public List<ModBodyCosmetics> Cosmetics;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.TestDriveResultAck)]
    public class TestDriveResultAckPacket : INetworkData
    {
        public int CarLoaderID;
        public bool Applied;
    }
}
