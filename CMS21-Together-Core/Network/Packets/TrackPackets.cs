using System;
using CMS21_Together_Core.Data.Enum;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.TrackRecord)]
public class TrackRecordPacket : INetworkData
{
	public GameScene Scene;
	public long Value;
}

[Serializable]
[NetworkPacket(PacketTypes.TrackRecordUpdate)]
public class TrackRecordUpdatePacket : INetworkData
{
	public GameScene Scene;
	public int PlayerId;
	public string PlayerName;
	public long Value;
	public bool IsGroupRecord;
	public bool IsPersonalBest;
}
