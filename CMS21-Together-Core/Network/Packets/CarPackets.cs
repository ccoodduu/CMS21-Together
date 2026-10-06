using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets
{
    // int[] has reference equality, so it can't be used as a Dictionary key directly.
    // Both client and server must use this to key CarState.SubParts consistently.
    public static class CarSubPartIdentity
    {
        public static string BuildKey(int[] partIndexPath) => string.Join(".", partIndexPath);
    }

    public static class PartKeys
    {
        public static string Body(int index) => $"b:{index}";

        public static string Sub(int[] partIndexPath) => $"s:{CarSubPartIdentity.BuildKey(partIndexPath)}";
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarSpawnRequest)]
    public class CarSpawnRequestPacket : INetworkData
    {
        public int CarLoaderID;
        public string CarToLoad;
        public int ConfigVersion;
        public int PlaceNo;
        // Used to determine if this is a job car, showroom car, etc.
        public bool IsJob;
        public int JobID;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarSpawnResponse)]
    public class CarSpawnResponsePacket : INetworkData
    {
        public int CarLoaderID;
        public string CarToLoad;
        public int ConfigVersion;
        public int PlaceNo;
        public bool IsJob;
        public int JobID;
        public int SpawnSeq;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarSpawnDelete)]
    public class CarSpawnDeletePacket : INetworkData
    {
        public int CarLoaderID;
    }

    // Sent back to the requesting client only, when a CarSpawnRequest fails
    // server-side validation. The sender already ran LoadCar natively (hybrid
    // design), so the client must undo it locally to stay in sync with the server.
    [Serializable]
    [NetworkPacket(PacketTypes.CarSpawnRejected)]
    public class CarSpawnRejectedPacket : INetworkData
    {
        public int CarLoaderID;
        public string Reason;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarSpawnAck)]
    public class CarSpawnAckPacket : INetworkData
    {
        public int CarLoaderID;
        public int SpawnSeq;
    }

    // Body-level part (CarPart: hood, doors, bumpers...). When a CarPart is taken
    // off in-game, the game itself converts it into an Item with this exact field
    // shape (Condition/Dent/Color/TintColor/PaintType/PaintData/Livery/Quality/
    // WashFactor...), so ModItem is reused as-is instead of duplicating those fields.
    // PartIndex (not PartName) is the identifier used for resolution: CarPart names
    // are not guaranteed unique among siblings, but carLoader.carParts[] is built
    // deterministically from the same car model on every client.
    [Serializable]
    public class CarBodyPartUpdatePacket
    {
        public int PartIndex;
        public string PartName;
        public bool Switched;
        public bool Unmounted;
        public string TunedID;
        public ModItem State;
        public int Revision;

        [Newtonsoft.Json.JsonIgnore]
        public string Key => PartKeys.Body(PartIndex);
    }

    // Mechanical sub-part (PartScript: pistons, belts, hoses...). Identified by a
    // sibling-index chain from the car root down to the target Transform, instead
    // of a name-based path: sibling PartScript objects are often identically named
    // (e.g. all pistons), which makes Transform.Find(namePath) ambiguous - this is
    // in fact a latent bug in the game's own native save path resolution
    // (PartScript.GetGameObjectPathWithoutRoot + Transform.Find). A pure index
    // chain resolved via Transform.GetChild(index) has no such ambiguity.
    [Serializable]
    public class CarSubPartUpdatePacket
    {
        public int[] PartIndexPath;
        public string PartId;
        public string TunedID;
        public bool Unmounted;
        public float Condition;
        public int Quality;
        public bool IsExamined;
        public bool IsPainted;
        public ModColor Color;
        public ModPaintType PaintType;
        public ModPaintData PaintData;
        public float Dust;
        public ModMountObjectData MountObjectData;
        public int Revision;

        [Newtonsoft.Json.JsonIgnore]
        public string Key => PartKeys.Sub(PartIndexPath);
    }

    [Serializable]
    public class PartPrecondition
    {
        public string Key;
        public bool WasUnmounted;
    }

    [Serializable]
    public class InventoryDelta
    {
        public List<ModItem> AddedItems = new List<ModItem>();
        public List<ModGroupItem> AddedGroups = new List<ModGroupItem>();
        public List<long> RemovedItemUids = new List<long>();
        public List<long> RemovedGroupUids = new List<long>();

        public bool IsEmpty => AddedItems.Count == 0 && AddedGroups.Count == 0 && RemovedItemUids.Count == 0 && RemovedGroupUids.Count == 0;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarPartsChange)]
    public class CarPartsChangePacket : INetworkData
    {
        public int CarLoaderID;
        public int SpawnSeq;
        public int TxId;
        public int Revision;
        public List<PartPrecondition> Preconditions = new List<PartPrecondition>();
        public List<CarBodyPartUpdatePacket> BodyParts = new List<CarBodyPartUpdatePacket>();
        public List<CarSubPartUpdatePacket> SubParts = new List<CarSubPartUpdatePacket>();
        public InventoryDelta InventoryDelta = new InventoryDelta();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarPartsChangeResult)]
    public class CarPartsChangeResultPacket : INetworkData
    {
        public int CarLoaderID;
        public int SpawnSeq;
        public int TxId;
        public bool Accepted;
        public string Reason;
        public int Revision;
        public List<CarBodyPartUpdatePacket> BodyParts = new List<CarBodyPartUpdatePacket>();
        public List<CarSubPartUpdatePacket> SubParts = new List<CarSubPartUpdatePacket>();
        public List<long> RestoreUids = new List<long>();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarPartClaim)]
    public class CarPartClaimPacket : INetworkData
    {
        public int CarLoaderID;
        public int SpawnSeq;
        public List<string> Keys = new List<string>();
        public bool Release;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarPartClaimUpdate)]
    public class CarPartClaimUpdatePacket : INetworkData
    {
        public const int Released = -1;

        public int CarLoaderID;
        public int SpawnSeq;
        public List<string> Keys = new List<string>();
        public int OwnerPlayerId = Released;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarPartsSnapshot)]
    public class CarPartsSnapshotPacket : INetworkData
    {
        public const int LiveSnapshot = 0;

        public int SnapshotId;
        public int CarLoaderID;
        public int SpawnSeq;
        public int Revision;
        public int BatchIndex;
        public bool IsLastBatch;
        public CarSpawnResponsePacket Spawn;
        public string EngineSwap;
        public List<CarBodyPartUpdatePacket> BodyParts = new List<CarBodyPartUpdatePacket>();
        public List<CarSubPartUpdatePacket> SubParts = new List<CarSubPartUpdatePacket>();
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarPartsResyncRequest)]
    public class CarPartsResyncRequestPacket : INetworkData
    {
        public int CarLoaderID;
        public int SpawnSeq;
        public string Reason;
    }
}
