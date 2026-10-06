using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Data
{
	public static class SharedDlc
	{
		private static readonly Dictionary<int, List<string>> byClient = new Dictionary<int, List<string>>();

		public static IReadOnlyList<string> Shared { get; private set; } = new List<string>();

		public static event Action<IReadOnlyList<string>> Changed;

		public static void Add(int clientId, IEnumerable<string> dlc)
		{
			byClient[clientId] = (dlc ?? Enumerable.Empty<string>())
				.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().OrderBy(d => d, StringComparer.Ordinal).ToList();
			Recompute();
		}

		public static void Remove(int clientId)
		{
			if (byClient.Remove(clientId)) Recompute();
		}

		public static IReadOnlyList<string> Of(int clientId) =>
			byClient.TryGetValue(clientId, out var dlc) ? dlc : null;

		public static IEnumerable<string> Describe()
		{
			yield return $"Shared DLC ({byClient.Count} players): {Format(Shared)}";
			foreach (var entry in byClient.OrderBy(e => e.Key))
				yield return $"  player {entry.Key}: {Format(entry.Value)}";
		}

		public static string Format(IEnumerable<string> dlc)
		{
			var list = dlc?.ToList() ?? new List<string>();
			return list.Count == 0 ? "none" : string.Join(", ", list);
		}

		private static void Recompute()
		{
			IEnumerable<string> next = null;
			foreach (var dlc in byClient.Values)
				next = next == null ? dlc : next.Intersect(dlc);
			var shared = (next ?? Enumerable.Empty<string>()).OrderBy(d => d, StringComparer.Ordinal).ToList();

			if (shared.SequenceEqual(Shared)) return;
			Shared = shared;
			Logger.Info($"Shared DLC set is now {Format(shared)} ({byClient.Count} players).");
			Changed?.Invoke(shared);
		}
	}
}
