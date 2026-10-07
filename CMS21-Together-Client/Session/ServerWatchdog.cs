using System.Diagnostics;
using System.Threading;
using CMS21_Together_Core.Logging;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Session;

public static class ServerWatchdog
{
	public const float SilenceTimeoutSeconds = 10f;
	private const float StallSeconds = 2f;

	private static readonly Stopwatch clock = Stopwatch.StartNew();
	private static long lastReceivedMs;
	private static float lastUpdate = -1f;

	public static void MarkReceived() => Interlocked.Exchange(ref lastReceivedMs, clock.ElapsedMilliseconds);

	public static void Update()
	{
		bool watching = Client.Instance != null && Client.Instance.IsConnected
		                && ConnectionStatus.State >= JoinStatus.Handshake && ConnectionStatus.State <= JoinStatus.InSession;
		if (!watching)
		{
			lastUpdate = -1f;
			return;
		}

		float now = Time.realtimeSinceStartup;
		if (lastUpdate < 0f || now - lastUpdate > StallSeconds) MarkReceived();
		lastUpdate = now;

		float silent = (clock.ElapsedMilliseconds - Interlocked.Read(ref lastReceivedMs)) / 1000f;
		if (silent < SilenceTimeoutSeconds) return;

		Log.Warn($"[Join] No message from the server for {silent:0.0} s.");
		ConnectionStatus.Fail(JoinFailure.Server, "Lost the connection to the server.");
	}
}
