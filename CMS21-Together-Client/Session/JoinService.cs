using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using UnityEngine.SceneManagement;

namespace CMS21Together.Session;

public static class JoinService
{
	private const float DirectHandshakeTimeout = 10f;
	private const float SteamHandshakeTimeout = 20f;

	private static readonly Dictionary<string, string> passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public static JoinTarget CurrentTarget { get; private set; }
	public static string CurrentPassword { get; private set; } = "";
	public static string CurrentAdminKey { get; private set; } = "";

	public static string RememberedPassword(JoinTarget target) =>
		target != null && passwords.TryGetValue(target.ToString(), out string password) ? password : "";

	public static bool Join(string text, out string error, string password = null, string adminKey = null)
	{
		if (!JoinTarget.TryParse(text, MainMod.PORT, out var target, out error)) return false;
		return Join(target, out error, password, adminKey);
	}

	public static bool Join(JoinTarget target, out string error, string password = null, string adminKey = null)
	{
		error = null;
		if (ConnectionStatus.IsBusy || ConnectionStatus.State == JoinStatus.InSession || Client.Instance.IsConnected)
		{
			error = "Already connected or connecting.";
			return false;
		}

		CurrentTarget = target;
		if (password != null) passwords[target.ToString()] = password;
		CurrentPassword = RememberedPassword(target);
		CurrentAdminKey = adminKey ?? PlayerSettings.AdminKey;
		bool steam = target.Kind == JoinTargetKind.Steam;
		ConnectionStatus.Begin(steam ? SteamHandshakeTimeout : DirectHandshakeTimeout);
		Log.Info($"[Join] Joining {target}");

		if (steam && !MainMod.IsSteamAvailable)
		{
			ConnectionStatus.Fail(JoinFailure.SteamUnavailable, "");
			return true;
		}

		try
		{
			if (steam) Client.Instance.ConnectToSteamServer(target.SteamServerId);
			else Client.Instance.ConnectToServer(target.Address);
		}
		catch (Exception ex)
		{
			ConnectionStatus.Fail(JoinFailure.Unreachable, ex.Message);
		}
		return true;
	}

	public static JoinTarget PendingConfirmation { get; private set; }
	public static JoinTarget QueuedJoin { get; private set; }

	public static void OnInSession()
	{
		if (CurrentTarget != null) PlayerSettings.LastJoinTarget = CurrentTarget.ToString();
		RichPresence.Publish();
	}

	public static bool HandleJoinString(string joinString, out string error)
	{
		if (!JoinTarget.TryParse(joinString, MainMod.PORT, out var target, out error)) return false;

		if (ConnectionStatus.IsBusy || ConnectionStatus.State == JoinStatus.InSession || Client.Instance.IsConnected)
		{
			PendingConfirmation = target;
			Log.Info($"[Join] Join request for {target} while in a session, waiting for confirmation.");
			return true;
		}
		if (SceneManager.GetActiveScene().name != "Menu")
		{
			QueuedJoin = target;
			return true;
		}
		return Join(target, out error);
	}

	public static void Answer(bool leaveAndJoin)
	{
		var target = PendingConfirmation;
		PendingConfirmation = null;
		if (!leaveAndJoin || target == null) return;

		QueuedJoin = target;
		if (Client.Instance.IsConnected) Client.Instance.Disconnect();
		ConnectionStatus.Set(JoinStatus.Idle);
		if (SceneManager.GetActiveScene().name != "Menu" && NotificationCenter.m_instance != null)
		{
			var center = NotificationCenter.m_instance;
			center.StartCoroutine(center.SelectSceneToLoad("Menu", SceneType.Menu, true, false));
		}
	}

	public static void QueueJoin(JoinTarget target) => QueuedJoin = target;

	public static void OnMenuReady()
	{
		var target = QueuedJoin;
		QueuedJoin = null;
		if (target == null) return;
		if (!Join(target, out string error)) Log.Warn($"[Join] Queued join to {target} failed: {error}");
	}

	public static void OnTransportClosed()
	{
		switch (ConnectionStatus.State)
		{
			case JoinStatus.Connecting:
				ConnectionStatus.Fail(JoinFailure.Unreachable, "The server closed the connection.");
				break;
			case JoinStatus.InSession:
				ConnectionStatus.Fail(JoinFailure.Server, "Lost the connection to the server.");
				break;
			case JoinStatus.Handshake:
			case JoinStatus.Loading:
			case JoinStatus.Syncing:
				ConnectionStatus.Fail(JoinFailure.Server, "The server closed the connection.");
				break;
		}
	}

	public static void ResetAfterFailure()
	{
		if (Client.Instance.IsConnected) Client.Instance.Disconnect();
		if (SceneManager.GetActiveScene().name == "Menu" || NotificationCenter.m_instance == null) return;

		var center = NotificationCenter.m_instance;
		center.StartCoroutine(center.SelectSceneToLoad("Menu", SceneType.Menu, true, false));
	}
}
