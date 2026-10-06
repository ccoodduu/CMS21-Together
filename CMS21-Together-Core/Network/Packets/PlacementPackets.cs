using System;
using System.Collections.Generic;

namespace CMS21_Together_Core.Network.Packets
{
    [Serializable]
    public class ParkedCar
    {
        public Guid Id;
        public string CarToLoad;
        public string UId;
        public int SaveVersion;
        public byte[] Data;
    }

    public enum ParkRefusal
    {
        None,
        ParkingFull,
        NoMoney,
        Invalid,
        JobCar,
        Taken
    }

    [Serializable]
    [NetworkPacket(PacketTypes.LifterActionRequest)]
    public class LifterActionRequestPacket : INetworkData
    {
        public int LifterIndex;
        public int FromState;
        public int ToState;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.LifterState)]
    public class LifterStatePacket : INetworkData
    {
        public int LifterIndex;
        public int State;
        public bool Instant;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarPlaceChangeRequest)]
    public class CarPlaceChangeRequestPacket : INetworkData
    {
        public int CarLoaderID;
        public int FromPlace;
        public int ToPlace;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarPlaceChanged)]
    public class CarPlaceChangedPacket : INetworkData
    {
        public int CarLoaderID;
        public int Place;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarParkRequest)]
    public class CarParkRequestPacket : INetworkData
    {
        public int RequestId;
        public int CarLoaderID = -1;
        public int PreferredSlot = -1;
        public ParkedCar Car;
        public int Price;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarParkResult)]
    public class CarParkResultPacket : INetworkData
    {
        public int RequestId;
        public bool Accepted;
        public ParkRefusal Reason;
        public int Slot = -1;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarUnparkRequest)]
    public class CarUnparkRequestPacket : INetworkData
    {
        public int Slot;
        public Guid ParkedCarId;
        public int CarLoaderID;
        public int Place;
        public int ConfigVersion;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ParkingMoveRequest)]
    public class ParkingMoveRequestPacket : INetworkData
    {
        public int From;
        public int To;
        public Guid FromId;
        public Guid ToId;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ParkingSlotUpdate)]
    public class ParkingSlotUpdatePacket : INetworkData
    {
        public int Slot;
        public ParkedCar Car;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ParkingState)]
    public class ParkingStatePacket : INetworkData
    {
        public int UnlockedLevels;
        public Dictionary<int, Guid> Occupied = new Dictionary<int, Guid>();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ParkingLevelUnlockRequest)]
    public class ParkingLevelUnlockRequestPacket : INetworkData
    {
        public int TargetLevels;
        public int Price;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ParkingResyncRequest)]
    public class ParkingResyncRequestPacket : INetworkData
    {
    }
}
