using System.Collections.Generic;
using CMS21_Together_Core.Data.Compatibility;

namespace CMS21Together.Compatibility;

public static class CompatOverrides
{
	public static string GameVersion { get; set; }
	public static List<string> Dlc { get; set; }
	public static string Protocol { get; set; }
	public static string ProtocolSent { get; set; }
	public static readonly List<ModReport> ExtraMods = new List<ModReport>();

	public static bool Any => GameVersion != null || Dlc != null || Protocol != null || ProtocolSent != null || ExtraMods.Count > 0;

	public static void Reset()
	{
		GameVersion = null;
		Dlc = null;
		Protocol = null;
		ProtocolSent = null;
		ExtraMods.Clear();
	}
}
