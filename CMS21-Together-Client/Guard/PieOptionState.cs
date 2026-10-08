using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using UnhollowerBaseLib;

namespace CMS21Together.Guard;

public sealed class PieBlockSource
{
	public string Name;
	public Func<string, string> Reason;
	public Action<string, string> Clicked;
}

public static class PieOptionState
{
	public const string GuardSource = "guard";

	private static readonly List<PieBlockSource> sources = new List<PieBlockSource>
	{
		new PieBlockSource { Name = GuardSource, Reason = id => FeatureGuard.WouldBlock(GuardKind.Pie, id) ? "not supported in multiplayer yet" : null },
	};

	private static readonly Dictionary<string, bool> wasEnabled = new Dictionary<string, bool>();
	private static readonly Dictionary<string, (PieBlockSource Source, string Reason)> blocks = new Dictionary<string, (PieBlockSource, string)>();
	private static IntPtr owner;

	public static void AddSource(PieBlockSource source)
	{
		sources.RemoveAll(s => s.Name == source.Name);
		sources.Add(source);
	}

	public static IReadOnlyDictionary<string, (PieBlockSource Source, string Reason)> Blocks => blocks;

	public static void Apply(PieMenuController controller, Il2CppStringArray ids)
	{
		var options = controller.options;
		if (options == null || ids == null) return;
		if (controller.Pointer != owner)
		{
			owner = controller.Pointer;
			wasEnabled.Clear();
			blocks.Clear();
		}
		foreach (string id in ids)
		{
			if (string.IsNullOrEmpty(id) || !options.ContainsKey(id)) continue;
			var block = Find(id);
			if (block.Source != null)
			{
				blocks[id] = block;
				bool enabled = options[id].Enabled;
				if (!wasEnabled.ContainsKey(id) || enabled) wasEnabled[id] = enabled;
				if (enabled) controller.SetEnableOption(id, false);
			}
			else
			{
				blocks.Remove(id);
				if (wasEnabled.TryGetValue(id, out bool enabled))
				{
					wasEnabled.Remove(id);
					controller.SetEnableOption(id, enabled);
				}
			}
		}
	}

	private static (PieBlockSource Source, string Reason) Find(string id)
	{
		foreach (var source in sources)
		{
			try
			{
				string reason = source.Reason(id);
				if (reason != null) return (source, reason);
			}
			catch (Exception e)
			{
				Log.Error($"[Guard] Pie option source {source.Name} failed on {id}: {e.Message}");
			}
		}
		return (null, null);
	}

	public static void Clicked(string id)
	{
		if (id == null || !blocks.TryGetValue(id, out var block) || block.Source.Clicked == null) return;
		string reason = block.Source.Reason(id);
		if (reason != null) block.Source.Clicked(id, reason);
	}
}
