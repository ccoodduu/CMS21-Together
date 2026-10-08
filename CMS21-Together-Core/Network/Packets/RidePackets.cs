using System;

namespace CMS21_Together_Core.Network.Packets;

public enum RideEndReason
{
	None,
	DriverReturned,
	DriverLeft,
	PassengerLeft,
	PassengerDidNotArrive,
	DriveCancelled
}

[Serializable]
[NetworkPacket(PacketTypes.RideUpdate)]
public class RideUpdatePacket : INetworkData
{
	public int DriverId;
	public int PassengerId;
	public int CarLoaderID;
	public bool Active;
	public RideEndReason Reason;
}
