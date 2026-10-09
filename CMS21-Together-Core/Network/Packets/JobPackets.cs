using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets
{
    public enum OrderActionType
    {
        Accept,
        Decline,
        AbortTake
    }

    public enum OrderRequestReason
    {
        None,
        NoCar,
        NotReady,
        Busy,
        Disabled,
        HarnessOff
    }

    public enum JobRemovedReason
    {
        Taken,
        Declined,
        Expired,
        Ended,
        TakeAborted
    }

    [Serializable]
    [NetworkPacket(PacketTypes.JobsState)]
    public class JobsStatePacket : INetworkData
    {
        public JobsState State;
        public bool IsGenerator;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.OrderGeneratorRole)]
    public class OrderGeneratorRolePacket : INetworkData
    {
        public bool IsGenerator;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.OrderGenerated)]
    public class OrderGeneratedPacket : INetworkData
    {
        public ModJob Job;
        public int MaxOpenOrders;
        [OptionalField] public int RequestId;
        [OptionalField] public OrderRequestReason Reason;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.OrderRequest)]
    public class OrderRequestPacket : INetworkData
    {
        public int RequestId;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.OrderAdded)]
    public class OrderAddedPacket : INetworkData
    {
        public ModJob Job;
        public float RemainingSeconds;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.OrderAction)]
    public class OrderActionPacket : INetworkData
    {
        public int JobId;
        public OrderActionType Action;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.OrderActionResult)]
    public class OrderActionResultPacket : INetworkData
    {
        public int JobId;
        public OrderActionType Action;
        public bool Approved;
        public string Reason;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.JobStarted)]
    public class JobStartedPacket : INetworkData
    {
        public int JobId;
        public int CarLoaderId;
        public ModJob Job;
        public ModMissionState Missions;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.JobProgress)]
    public class JobProgressPacket : INetworkData
    {
        public int JobId;
        public List<string> FoundParts = new List<string>();
        public List<int> TaskMoneySpent = new List<int>();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.JobEndRequest)]
    public class JobEndRequestPacket : INetworkData
    {
        public int JobId;
        public int CarLoaderId;
        public int Payout;
        public int Xp;
        public bool IsCompleted;
        public bool IsMission;
        public ModMissionState Missions;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.JobRemoved)]
    public class JobRemovedPacket : INetworkData
    {
        public int JobId;
        public JobRemovedReason Reason;
        public int CarLoaderId = -1;
        public bool IsCompleted;
        public ModMissionState Missions;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.JobStatsAward)]
    public class JobStatsAwardPacket : INetworkData
    {
        public int JobId;
        public List<string> Stats = new List<string>();
        public bool MissionFinished;
    }
}
