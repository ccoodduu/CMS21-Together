using System.Collections.Generic;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Server.Data.Presence
{
	/// <summary>The drive each player has running, never saved. Callers hold <see cref="GameDataManager.StateLock"/>.</summary>
	public static class ActiveDrives
	{
		public class Drive
		{
			public CarDriveStartPacket Start;
			public CarDriveStatePacket Latest;
			public GameScene Scene => Start.Scene;
		}

		private static readonly Dictionary<int, Drive> drives = new Dictionary<int, Drive>();

		public static IEnumerable<KeyValuePair<int, Drive>> All => drives;

		public static Drive Get(int playerId) => drives.TryGetValue(playerId, out var drive) ? drive : null;

		public static void Begin(CarDriveStartPacket start) => drives[start.PlayerId] = new Drive { Start = start };

		public static bool End(int playerId, out Drive drive)
		{
			if (!drives.TryGetValue(playerId, out drive)) return false;
			drives.Remove(playerId);
			return true;
		}

		public static void Clear() => drives.Clear();
	}
}
