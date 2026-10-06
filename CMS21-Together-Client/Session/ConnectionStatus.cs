using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using UnityEngine;

namespace CMS21Together.Session;

public enum JoinStatus
{
	Idle,
	Connecting,
	Handshake,
	Loading,
	Syncing,
	InSession,
	Failed,
	Disconnected
}

public enum JoinFailure
{
	None,
	Unreachable,
	Timeout,
	SteamUnavailable,
	SteamFailed,
	Server
}

public static class ConnectionStatus
{
	public static JoinStatus State { get; private set; } = JoinStatus.Idle;
	public static JoinFailure Failure { get; private set; }
	public static DisconnectReason ServerReason { get; private set; }
	public static string Message { get; private set; } = "";
	public static bool MessagePending { get; set; }

	private static float handshakeDeadline;

	public static bool IsBusy => State >= JoinStatus.Connecting && State <= JoinStatus.Syncing;

	public static string LastReason => Failure == JoinFailure.Server ? ServerReason.ToString() : Failure.ToString();

	public static void Begin(float handshakeTimeout)
	{
		Failure = JoinFailure.None;
		ServerReason = DisconnectReason.None;
		Message = "";
		MessagePending = false;
		handshakeDeadline = Time.realtimeSinceStartup + handshakeTimeout;
		Set(JoinStatus.Connecting);
	}

	public static void OnLocalDisconnect()
	{
		if (IsBusy || State == JoinStatus.InSession) Set(JoinStatus.Idle);
	}

	public static void Set(JoinStatus state)
	{
		if (State == state) return;
		if (State == JoinStatus.InSession && state == JoinStatus.Syncing) return;
		if ((State == JoinStatus.Failed || State == JoinStatus.Disconnected) && state != JoinStatus.Idle && state != JoinStatus.Connecting) return;
		if (State == JoinStatus.InSession) RichPresence.Clear();
		State = state;
		Log.Info($"[Join] {state}");
	}

	public static bool Fail(JoinFailure failure, string message, DisconnectReason serverReason = DisconnectReason.None)
	{
		if (State == JoinStatus.Failed || State == JoinStatus.Disconnected || State == JoinStatus.Idle) return false;

		bool inSession = State == JoinStatus.InSession;
		if (inSession) RichPresence.Clear();
		Failure = failure;
		ServerReason = serverReason;
		Message = ConnectionMessages.For(failure, serverReason, message);
		MessagePending = true;
		State = inSession ? JoinStatus.Disconnected : JoinStatus.Failed;
		Log.Warn($"[Join] {State}: {LastReason} - {Message}");
		JoinService.ResetAfterFailure();
		return true;
	}

	public static void Update()
	{
		if (State == JoinStatus.Connecting && Time.realtimeSinceStartup > handshakeDeadline)
			Fail(JoinFailure.Timeout, "");
	}
}
