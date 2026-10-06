using System;
using System.Collections;
using System.Linq;
using CMS21_Together_Core.Logging;
using MelonLoader;
using Steamworks;
using UnityEngine;

namespace CMS21Together.Session;

public static class JoinRequests
{
	private const float MenuSettleSeconds = 2f;

	public static void Initialize()
	{
		string coldStart = FindColdStartTarget();
		if (coldStart != null)
		{
			if (JoinTarget.TryParse(coldStart, MainMod.PORT, out var target, out string error))
			{
				Log.Info($"[Join] Started with join target {target}; joining once the menu is ready.");
				JoinService.QueueJoin(target);
			}
			else
			{
				Log.Warn($"[Join] Ignoring start argument '{coldStart}': {error}");
			}
		}

		if (MainMod.IsSteamAvailable)
		{
			SteamFriends.OnGameRichPresenceJoinRequested += (friend, connect) =>
			{
				Log.Info($"[Join] Steam join request from {friend.Name}: {connect}");
				if (!JoinService.HandleJoinString(connect, out string requestError))
					Log.Warn($"[Join] Cannot join '{connect}': {requestError}");
			};
		}
	}

	public static void OnMenuInitialized()
	{
		if (JoinService.QueuedJoin != null) MelonCoroutines.Start(JoinWhenSettled());
	}

	private static IEnumerator JoinWhenSettled()
	{
		yield return new WaitForSeconds(MenuSettleSeconds);
		JoinService.OnMenuReady();
	}

	private static string FindColdStartTarget()
	{
		string steamLine = null;
		if (MainMod.IsSteamAvailable)
		{
			try { steamLine = SteamApps.CommandLine; }
			catch (Exception) { }
		}

		var args = !string.IsNullOrWhiteSpace(steamLine)
			? steamLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
			: Environment.GetCommandLineArgs();

		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "+connect" && i + 1 < args.Length) return args[i + 1];
		}
		return args.FirstOrDefault(a => a.StartsWith(JoinTarget.JoinStringPrefix, StringComparison.OrdinalIgnoreCase));
	}
}
