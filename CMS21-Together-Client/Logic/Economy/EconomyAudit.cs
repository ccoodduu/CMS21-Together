using System;
using System.Collections.Generic;
using CMS.UI;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;

namespace CMS21Together.Logic.Economy;

public static class EconomyAudit
{
	private const int UnattributedKept = 20;

	public class UnattributedCall
	{
		public DateTime UtcTime;
		public string Kind;
		public int Amount;
		public string Scene;
		public string Mode;
		public string Window;
	}

	public static readonly Dictionary<string, int> Sent = new Dictionary<string, int>();
	public static readonly Dictionary<string, int> CoveredCalls = new Dictionary<string, int>();
	public static readonly Dictionary<string, int> SuppressedCalls = new Dictionary<string, int>();
	public static readonly Dictionary<string, int> Refused = new Dictionary<string, int>();
	public static readonly Queue<UnattributedCall> LastUnattributed = new Queue<UnattributedCall>();
	public static int Unattributed { get; private set; }

	public static void Count(Dictionary<string, int> counts, string name) => counts[name] = counts.TryGetValue(name, out int count) ? count + 1 : 1;

	public static void Drop(string kind, int amount)
	{
		Unattributed++;
		var call = new UnattributedCall
		{
			UtcTime = DateTime.UtcNow, Kind = kind, Amount = amount, Scene = ClientScene.LocalScene.ToString(),
			Mode = SafeMode(), Window = TopWindow(),
		};
		LastUnattributed.Enqueue(call);
		while (LastUnattributed.Count > UnattributedKept) LastUnattributed.Dequeue();
		Log.Warn($"[Economy] Unattributed {kind} change {amount} dropped (scene {call.Scene}, mode {call.Mode}, window {call.Window}).");
	}

	public static void ResetUnattributed()
	{
		Unattributed = 0;
		LastUnattributed.Clear();
	}

	private static string SafeMode()
	{
		try { return GameMode.Get()?.GetCurrentMode().ToString() ?? "none"; }
		catch (Exception) { return "?"; }
	}

	private static string TopWindow()
	{
		try
		{
			var window = WindowManager.Instance?.GetLastOpenedWindow();
			return window == null ? "none" : window.gameObject.name;
		}
		catch (Exception)
		{
			return "?";
		}
	}
}
