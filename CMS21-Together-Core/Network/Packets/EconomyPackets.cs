using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets
{
    [Serializable]
    public enum EconomyReason
    {
        Work = 0,
        TravelFee = 1,
        TravelFeeLegacy = 2,
        FluidSpill = 3,
        FluidRefill = 4,
        PaintCar = 5,
        WashBeforePaint = 6,
        WashBeforeTint = 7,
        Tint = 8,
        Welder = 9,
        InteriorDetailing = 10,
        PartRepair = 11,
        CrateMoney = 12,
        CrateScrap = 13,
        CrateExp = 14,
        SkillReset = 15,
        CarSale = 16,
        ScrapItem = 17,
        ScrapPerCondition = 18,
        ScrapUpgrade = 19,
        LicensePlates = 20,
        BarnMap = 21
    }

    [Serializable]
    public enum EconomyRefusal
    {
        None = 0,
        Invalid = 1,
        NoMoney = 2,
        NoScraps = 3,
        Busy = 4,
        Gone = 5
    }

    [Serializable]
    [NetworkPacket(PacketTypes.EconomyRequest)]
    public class EconomyRequestPacket : INetworkData
    {
        public int RequestId;
        public EconomyReason Reason;
        public int Money;
        public int Scraps;
        public int Exp;
        public int Arg;
        public int Arg2;
        public long ItemUid;
        public int CarLoaderId = -1;
        public int SpawnSeq;
        public int ParkingSlot = -1;
        public string CarId;
        public List<ModItem> Items;
    }

    [Serializable]
    [NetworkPacket(PacketTypes.EconomyResult)]
    public class EconomyResultPacket : INetworkData
    {
        public int RequestId;
        public EconomyReason Reason;
        public bool Accepted;
        public EconomyRefusal Refusal;
        public int Money;
        public int Scraps;
    }
}
