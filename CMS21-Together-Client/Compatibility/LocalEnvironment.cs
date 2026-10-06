using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Compatibility;
using UnityEngine;

namespace CMS21Together.Compatibility;

public class DlcInfo
{
	public string ProductId;
	public string Name;
	public bool Owned;
}

public static class LocalEnvironment
{
	public static string GameVersion => CompatOverrides.GameVersion ?? ReadGameVersion();

	public static List<string> OwnedDlc => CompatOverrides.Dlc != null
		? CompatOverrides.Dlc.OrderBy(d => d, StringComparer.Ordinal).ToList()
		: (ReadDlc() ?? new List<DlcInfo>()).Where(d => d.Owned).Select(d => d.ProductId).OrderBy(d => d, StringComparer.Ordinal).ToList();

	public static string ProtocolHashValue => CompatOverrides.Protocol ?? ProtocolHash.Value;

	public static string SentProtocolHash => CompatOverrides.ProtocolSent ?? ProtocolHashValue;

	public static string BuildVersion
	{
		get
		{
			try { return GameSettings.BuildVersion; }
			catch (Exception) { return null; }
		}
	}

	private static string ReadGameVersion()
	{
		string build = BuildVersion;
		return string.IsNullOrWhiteSpace(build) ? Application.version : build;
	}

	public static List<DlcInfo> ReadDlc()
	{
		try
		{
			var platform = Singleton<GameManager>.Instance?.PlatformManager;
			if (platform == null) return null;
			var dlcs = platform.GetDLCs();
			if (dlcs == null) return null;

			var result = new List<DlcInfo>();
			for (int i = 0; i < dlcs.Length; i++)
			{
				var dlc = dlcs[i];
				if (dlc == null) continue;
				result.Add(new DlcInfo { ProductId = dlc.ProductId, Name = dlc.Name, Owned = dlc.Owned });
			}
			return result;
		}
		catch (Exception)
		{
			return null;
		}
	}
}
