using System;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.BugReportRequest)]
public class BugReportRequestPacket : INetworkData
{
	public string Id;
	public string Note = "";
}

[Serializable]
[NetworkPacket(PacketTypes.BugReportCollect)]
public class BugReportCollectPacket : INetworkData
{
	public string Id;
}

[Serializable]
[NetworkPacket(PacketTypes.BugReportResult)]
public class BugReportResultPacket : INetworkData
{
	public string Id;
	public string ServerFile = "";
	public string Error = "";
}
