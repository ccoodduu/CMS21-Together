using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using Steamworks;
using UnityEngine;

namespace CMS21Together.Session;

public class FriendRow
{
	public ulong Id;
	public string Name;
	public bool PlayingThisGame;
	public string JoinString;

	public bool CanJoin => !string.IsNullOrEmpty(JoinString);
}

public static class SteamFriendsList
{
	private const float RefreshInterval = 5f;

	private static float nextRefresh;

	public static List<FriendRow> Rows { get; private set; } = new List<FriendRow>();
	public static string Status { get; private set; } = "";

	public static bool Available => MainMod.IsSteamAvailable;

	public static bool CanInvite => Available && ConnectionStatus.State == JoinStatus.InSession && RichPresence.JoinString != null;

	public static void RefreshIfDue()
	{
		if (Time.realtimeSinceStartup < nextRefresh) return;
		nextRefresh = Time.realtimeSinceStartup + RefreshInterval;
		Refresh();
	}

	public static void Refresh()
	{
		if (!Available)
		{
			Rows = new List<FriendRow>();
			Status = "Steam unavailable: start the game through Steam to see friends.";
			return;
		}

		try
		{
			Rows = SteamFriends.GetFriends()
				.Where(f => f.IsOnline)
				.Select(f => new FriendRow
				{
					Id = f.Id.Value,
					Name = f.Name,
					PlayingThisGame = f.IsPlayingThisGame,
					JoinString = JoinStringOf(f)
				})
				.OrderByDescending(r => r.CanJoin)
				.ThenByDescending(r => r.PlayingThisGame)
				.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();
			Status = Rows.Count == 0 ? "No friends online." : "";
		}
		catch (Exception ex)
		{
			Rows = new List<FriendRow>();
			Status = "Could not read the Steam friends list.";
			Log.Warn($"[Friends] {Status} {ex.Message}");
		}
	}

	public static bool Join(ulong friendId, out string error)
	{
		error = null;
		var row = Rows.FirstOrDefault(r => r.Id == friendId);
		if (row == null || !row.CanJoin)
		{
			error = "This friend is not in a session you can join.";
			return false;
		}
		Log.Info($"[Friends] Joining {row.Name}: {row.JoinString}");
		return JoinService.HandleJoinString(row.JoinString, out error);
	}

	public static bool Invite(ulong friendId, out string error)
	{
		error = null;
		var row = Rows.FirstOrDefault(r => r.Id == friendId);
		if (row == null || !CanInvite)
		{
			error = "Invites need Steam and a session friends can join.";
			return false;
		}
		try
		{
			if (new Friend(friendId).InviteToGame(RichPresence.JoinString))
			{
				Log.Info($"[Friends] Invited {row.Name}.");
				return true;
			}
			error = $"Steam did not send the invite to {row.Name}.";
		}
		catch (Exception ex)
		{
			error = $"Invite failed: {ex.Message}";
		}
		Log.Warn($"[Friends] {error}");
		return false;
	}

	private static string JoinStringOf(Friend friend)
	{
		if (!friend.IsPlayingThisGame) return null;
		string connect = friend.GetRichPresence("connect");
		return connect != null && connect.StartsWith(JoinTarget.JoinStringPrefix, StringComparison.OrdinalIgnoreCase) ? connect : null;
	}
}
