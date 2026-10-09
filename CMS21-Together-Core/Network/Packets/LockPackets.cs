using System;
using System.Collections.Generic;
using System.Linq;

namespace CMS21_Together_Core.Network.Packets
{
    public enum CarLockKind : byte
    {
        PartUnmount,
        PartMount,
        GroupUnmount,
        GroupMount,
        BodyPart,
        Fluid,
        OilDrain,
        Crane,
        Lift,
        Move,
        Tune
    }

    public enum CarLockRefusal : byte
    {
        None,
        Held,
        Away,
        NotReady,
        Stale,
        Invalid,
        Item,
        CarBusy
    }

    public static class LockScope
    {
        public const string Connected = "connected";
        public const string Part = "part";
    }

    public static class LockKeys
    {
        public const string Car = "car";
        public const string Engine = "engine";
        public const string Tune = "tune";

        public static string Fluid(string fluidType, int id) => $"f:{fluidType}.{id}";

        public static bool IsSub(string key) => key != null && key.StartsWith("s:", StringComparison.Ordinal);

        public static bool IsBody(string key) => key != null && key.StartsWith("b:", StringComparison.Ordinal);

        public static bool IsPart(string key) => IsSub(key) || IsBody(key);

        public static bool IsFluid(string key) => key != null && key.StartsWith("f:", StringComparison.Ordinal);

        public static string Place(int place) => $"p:{place}";

        public static bool IsPlace(string key) => key != null && key.StartsWith("p:", StringComparison.Ordinal);

        public static bool IsWellFormed(string key)
        {
            if (key == Car || key == Engine || key == Tune) return true;
            if (IsBody(key)) return int.TryParse(key.Substring(2), out int index) && index >= 0;
            if (IsSub(key)) return Segments(key) != null;
            if (!IsFluid(key)) return false;
            int dot = key.LastIndexOf('.');
            return dot > 2 && int.TryParse(key.Substring(dot + 1), out int id) && id >= 0;
        }

        public static int[] Segments(string subKey)
        {
            if (!IsSub(subKey) || subKey.Length == 2) return null;
            var parts = subKey.Substring(2).Split('.');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out result[i])) return null;
            return result;
        }

        public static bool IsAncestor(string ancestor, string key)
        {
            var outer = Segments(ancestor);
            var inner = Segments(key);
            if (outer == null || inner == null || outer.Length >= inner.Length) return false;
            for (int i = 0; i < outer.Length; i++)
                if (outer[i] != inner[i]) return false;
            return true;
        }

        public static IEnumerable<string> AncestorCandidates(string subKey)
        {
            var segments = Segments(subKey);
            if (segments == null) yield break;
            for (int length = segments.Length - 1; length > 0; length--)
                yield return "s:" + string.Join(".", segments.Take(length));
        }
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarLockRequest)]
    public class CarLockRequestPacket : INetworkData
    {
        public int RequestId;
        public int CarLoaderID;
        public int SpawnSeq;
        public CarLockKind Kind;
        public List<string> X = new List<string>();
        public List<string> S = new List<string>();
        public List<long> Items = new List<long>();
        public int ExtendLockId;
        public int OtherLoaderID = -1;
        public int OtherSpawnSeq;
        public int Place = -1;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarLockResult)]
    public class CarLockResultPacket : INetworkData
    {
        public int RequestId;
        public int LockId;
        public bool Granted;
        public CarLockRefusal Refusal;
        public int HolderPlayerId = -1;
        public string ConflictKey;
        [System.Runtime.Serialization.OptionalField] public CarLockKind HolderKind;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarLockUpdate)]
    public class CarLockUpdatePacket : INetworkData
    {
        public const int Released = -1;

        public int LockId;
        public int LinkedLockId;
        public int CarLoaderID;
        public int SpawnSeq;
        public int OwnerPlayerId = Released;
        public CarLockKind Kind;
        public byte Phase;
        public List<string> X = new List<string>();
        public List<string> S = new List<string>();
        public List<long> Items = new List<long>();
        public int Place = -1;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarLockRelease)]
    public class CarLockReleasePacket : INetworkData
    {
        public int LockId;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.CarLockRenew)]
    public class CarLockRenewPacket : INetworkData
    {
        public List<int> LockIds = new List<int>();
    }
}
