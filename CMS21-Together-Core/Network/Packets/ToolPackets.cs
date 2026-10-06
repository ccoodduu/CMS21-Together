using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets
{
    [Serializable]
    [NetworkPacket(PacketTypes.ToolSlotUpdate)]
    public class ToolSlotUpdatePacket : INetworkData
    {
        public ToolSlotState State;
        public long ExpectedUid;
        public int ClientSeq;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ToolSlotRejected)]
    public class ToolSlotRejectedPacket : INetworkData
    {
        public ToolSlotState Current;
        public string Reason;
        public int ClientSeq;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ToolSlotProperty)]
    public class ToolSlotPropertyPacket : INetworkData
    {
        public ModToolId Tool;
        public ToolProperty Property;
        public float Value;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ToolPartChange)]
    public class ToolPartChangePacket : INetworkData
    {
        public ModToolId Tool;
        public long EngineUid;
        public int TxId;
        public List<PartPrecondition> Preconditions = new List<PartPrecondition>();
        public List<CarSubPartUpdatePacket> SubParts = new List<CarSubPartUpdatePacket>();
        public InventoryDelta Delta = new InventoryDelta();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ToolPartChangeResult)]
    public class ToolPartChangeResultPacket : INetworkData
    {
        public ModToolId Tool;
        public long EngineUid;
        public int TxId;
        public bool Accepted;
        public string Reason;
        public List<CarSubPartUpdatePacket> SubParts = new List<CarSubPartUpdatePacket>();
        public List<long> RestoreUids = new List<long>();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ToolPosition)]
    public class ToolPositionPacket : INetworkData
    {
        public const int DefaultPosition = -1;

        public int IoSpecialType;
        public int CarPlace = DefaultPosition;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ToolsState)]
    public class ToolsStatePacket : INetworkData
    {
        public Dictionary<ModToolId, ToolSlotState> Slots = new Dictionary<ModToolId, ToolSlotState>();
        public Dictionary<int, int> Positions = new Dictionary<int, int>();
        public Dictionary<ModToolId, int> Claims = new Dictionary<ModToolId, int>();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ToolClaim)]
    public class ToolClaimPacket : INetworkData
    {
        public ModToolId Tool;
        public bool Release;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.ToolClaimUpdate)]
    public class ToolClaimUpdatePacket : INetworkData
    {
        public const int Released = -1;

        public ModToolId Tool;
        public int OwnerPlayerId = Released;
    }
}
