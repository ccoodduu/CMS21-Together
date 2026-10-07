using System;

namespace CMS21_Together_Core.Network.Packets;

public enum ActivityKind
{
	None,
	Unmount,
	Mount,
	BodyPanel,
	Examine,
	Fluid,
	CarTool,
	Machine,
	Interior
}

[Serializable]
public class PlayerActivityState
{
	public const int NoCar = -1;
	public const int NoTool = -1;
	public const byte ProgressSteps = 16;

	public ActivityKind Kind;
	public int CarLoaderID = NoCar;
	public string PartKey;
	public int ToolType = NoTool;
	public int ModTool = NoTool;
	public byte Progress;

	public static PlayerActivityState Idle() => new PlayerActivityState();

	public bool IsIdle => Kind == ActivityKind.None;

	public float ProgressFraction => Progress / (float)ProgressSteps;

	public static byte QuantizeProgress(float fraction)
	{
		if (float.IsNaN(fraction) || fraction <= 0f) return 0;
		if (fraction >= 1f) return ProgressSteps;
		return (byte)Math.Round(fraction * ProgressSteps);
	}

	public PlayerActivityState Clone() => (PlayerActivityState)MemberwiseClone();

	public bool SameAs(PlayerActivityState other) =>
		other != null && Kind == other.Kind && CarLoaderID == other.CarLoaderID && PartKey == other.PartKey
		&& ToolType == other.ToolType && ModTool == other.ModTool && Progress == other.Progress;

	public override string ToString() =>
		Kind == ActivityKind.None ? "None" : $"{Kind} car {CarLoaderID} part {PartKey ?? "-"} tool {ToolType}/{ModTool} progress {Progress}/{ProgressSteps}";
}

[Serializable]
[NetworkPacket(PacketTypes.PlayerActivity)]
public class PlayerActivityPacket : INetworkData
{
	public int PlayerId;
	public PlayerActivityState State;
}
