using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21Together.Network;
using CMS21Together.Persistence;

namespace CMS21Together.Guard;

public enum GuardKind
{
	Window,
	Pie,
	Mode,
	Scene
}

public enum GuardDecision
{
	Allow,
	Block
}

public enum GuardMode
{
	Enforce,
	LogOnly,
	Off
}

public readonly struct GuardBlock
{
	public GuardBlock(DateTime utc, string key, bool enforced)
	{
		Utc = utc;
		Key = key;
		Enforced = enforced;
	}

	public DateTime Utc { get; }
	public string Key { get; }
	public bool Enforced { get; }

	public override string ToString() => $"{Utc:HH:mm:ss.fff} {(Enforced ? "blocked" : "would block")} {Key}";
}

public static class FeatureGuard
{
	public const int RingSize = 200;
	private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan MessageInterval = TimeSpan.FromSeconds(2);

	private static readonly Queue<GuardBlock> blocks = new Queue<GuardBlock>();
	private static readonly Dictionary<string, DateTime> lastLogged = new Dictionary<string, DateTime>();
	private static readonly HashSet<string> runtimeAllow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	private static DateTime lastMessage = DateTime.MinValue;
	private static int bypassDepth;

	public static GuardMode? ModeOverride { get; set; }

	public static GuardMode Mode => ModeOverride ?? GuardSettings.Mode;

	public static bool IsSessionActive => SessionGuard.Active && Client.Instance != null && Client.Instance.IsConnected;

	public static bool IsBypassed => bypassDepth > 0;

	public static (string Key, GuardDecision Decision)? LastDecision { get; private set; }

	public static IReadOnlyCollection<GuardBlock> Blocks => blocks;

	public static string Key(GuardKind kind, string id) => $"{kind}:{id}";

	public static IDisposable Bypass()
	{
		bypassDepth++;
		return new BypassScope();
	}

	public static void AllowAtRuntime(string key) => runtimeAllow.Add(key);

	public static bool IsAllowedByRules(GuardKind kind, string id)
	{
		string key = Key(kind, id);
		if (GuardSettings.Denied(key)) return false;
		return runtimeAllow.Contains(key) || GuardSettings.Allowed(key) || GuardRules.IsAllowed(kind, id);
	}

	public static bool WouldBlock(GuardKind kind, string id) =>
		IsSessionActive && !IsBypassed && Mode == GuardMode.Enforce && !IsAllowedByRules(kind, id);

	public static GuardDecision Decide(GuardKind kind, string id, bool canBlock = true)
	{
		string key = Key(kind, id);
		var decision = Evaluate(kind, id, key, canBlock);
		LastDecision = (key, decision);
		return decision;
	}

	private static GuardDecision Evaluate(GuardKind kind, string id, string key, bool canBlock)
	{
		if (!IsSessionActive || IsBypassed) return GuardDecision.Allow;
		var mode = Mode;
		if (mode == GuardMode.Off || IsAllowedByRules(kind, id)) return GuardDecision.Allow;

		bool enforced = mode == GuardMode.Enforce && canBlock;
		Record(key, enforced);
		if (!enforced) return GuardDecision.Allow;

		ShowMessage(GuardRules.Label(kind, id));
		return GuardDecision.Block;
	}

	private static void Record(string key, bool enforced)
	{
		var now = DateTime.UtcNow;
		blocks.Enqueue(new GuardBlock(now, key, enforced));
		while (blocks.Count > RingSize) blocks.Dequeue();

		if (lastLogged.TryGetValue(key, out var last) && now - last < LogInterval) return;
		lastLogged[key] = now;
		Log.Warn($"[Guard] {(enforced ? "Blocked" : "Would block")} {key} (not supported in multiplayer yet).");
	}

	private static void ShowMessage(string feature)
	{
		var now = DateTime.UtcNow;
		if (now - lastMessage < MessageInterval) return;
		lastMessage = now;
		GuardNotice.Show($"{feature} is not supported in multiplayer yet");
	}

	public static void LogSessionStart()
	{
		Log.Info($"[Guard] Mode {Mode}; allow '{GuardSettings.AllowRaw}'; deny '{GuardSettings.DenyRaw}'"
		         + (runtimeAllow.Count > 0 ? $"; runtime allow '{string.Join(";", runtimeAllow)}'" : "") + ".");
	}

	private sealed class BypassScope : IDisposable
	{
		private bool disposed;

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			bypassDepth--;
		}
	}
}
