using System;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.CoopPing)]
public class CoopPingPacket : INetworkData
{
	public const int NoCar = -1;
	public const int MaxKeyLength = 128;

	public int PlayerId;
	public GameScene Scene;
	public int CarLoaderID = NoCar;
	public string PartKey;
	public Vector3Serializable Position;

	public bool IsPart => CarLoaderID != NoCar && !string.IsNullOrEmpty(PartKey);

	public override string ToString() =>
		IsPart ? $"part {PartKey} on car {CarLoaderID} in {Scene}" : $"spot ({Position?.X:0.##}, {Position?.Y:0.##}, {Position?.Z:0.##}) in {Scene}";
}
