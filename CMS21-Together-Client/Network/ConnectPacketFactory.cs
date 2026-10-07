using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.Compatibility;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Compatibility;
using CMS21Together.Data;
using CMS21Together.Persistence;
using CMS21Together.Session;

namespace CMS21Together.Network;

public static class ConnectPacketFactory
{
	public static ConnectPacket Build()
	{
		var mods = CollectMods();
		var packet = new ConnectPacket
		{
			playerID = Client.Instance.ID,
			username = PlayerSettings.PlayerName,
			message = "",
			modVersion = ClientVersion.Current,
			gameVersion = LocalEnvironment.GameVersion,
			protocolHash = LocalEnvironment.SentProtocolHash,
			dlc = LocalEnvironment.OwnedDlc,
			mods = mods,
			password = JoinService.CurrentPassword,
			adminKey = JoinService.CurrentAdminKey,
			playerKey = PlayerIdentity.Key,
		};

		var verdicts = new ModClassifier(ModClassifierRules.Default).ClassifyAll(mods);
		Log.Info($"[Compat] Sending game {packet.gameVersion}, protocol {packet.protocolHash}, DLC [{string.Join(", ", packet.dlc)}], " +
		         $"mods: {(verdicts.Count == 0 ? "none" : string.Join("; ", verdicts))}{(CompatOverrides.Any ? " (harness overrides active)" : "")}");
		Log.Info($"[Identity] Sending player key {PlayerIdentity.Short(packet.playerKey)}{(string.IsNullOrEmpty(PlayerIdentity.Override) ? "" : " (harness override)")}.");
		return packet;
	}

	public static List<ModReport> CollectMods()
	{
		List<ModReport> mods;
		try
		{
			mods = ModInventory.Collect();
		}
		catch (Exception ex)
		{
			Log.Warn($"[Compat] Could not list the loaded mods: {ex.Message}");
			mods = new List<ModReport>();
		}
		mods.AddRange(CompatOverrides.ExtraMods);
		return mods;
	}
}
