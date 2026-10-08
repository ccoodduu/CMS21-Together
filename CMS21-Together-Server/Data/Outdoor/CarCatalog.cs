using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Data.Outdoor
{
	public static class CarCatalog
	{
		private static readonly Dictionary<int, Dictionary<OutdoorCatalogScene, List<CatalogEntry>>> byClient =
			new Dictionary<int, Dictionary<OutdoorCatalogScene, List<CatalogEntry>>>();
		private static readonly Dictionary<OutdoorCatalogScene, List<CatalogEntry>> shared = new Dictionary<OutdoorCatalogScene, List<CatalogEntry>>();

		public static Func<IReadOnlyList<string>> SharedDlcSet = () => SharedDlc.Shared;

		public static int Reporters => byClient.Count;

		public static IReadOnlyList<CatalogEntry> For(OutdoorCatalogScene scene) =>
			shared.TryGetValue(scene, out var entries) ? entries : (IReadOnlyList<CatalogEntry>)new List<CatalogEntry>();

		public static void Report(int clientId, OutdoorCatalogPacket packet)
		{
			var scenes = new Dictionary<OutdoorCatalogScene, List<CatalogEntry>>();
			foreach (var scene in (packet?.Scenes ?? new Dictionary<OutdoorCatalogScene, List<CatalogEntry>>()))
			{
				scenes[scene.Key] = (scene.Value ?? new List<CatalogEntry>())
					.Where(e => e != null && !string.IsNullOrEmpty(e.CarId))
					.GroupBy(e => e.Key).Select(g => g.First())
					.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
			}
			byClient[clientId] = scenes;
			Logger.Info($"[Outdoor] Catalog from {clientId}: {string.Join(", ", scenes.OrderBy(s => s.Key).Select(s => $"{s.Key} {s.Value.Count}"))}.");
			Recompute();
		}

		public static void Remove(int clientId)
		{
			if (byClient.Remove(clientId)) Recompute();
		}

		public static void Reset()
		{
			byClient.Clear();
			shared.Clear();
		}

		public static void Recompute()
		{
			var dlc = new HashSet<string>(SharedDlcSet() ?? new List<string>());
			foreach (OutdoorCatalogScene scene in System.Enum.GetValues(typeof(OutdoorCatalogScene)))
			{
				List<CatalogEntry> common = null;
				foreach (var report in byClient.Values)
				{
					var entries = report.TryGetValue(scene, out var list) ? list : new List<CatalogEntry>();
					if (common == null)
					{
						common = entries.ToList();
						continue;
					}
					var keys = new HashSet<string>(entries.Select(e => e.Key));
					common = common.Where(e => keys.Contains(e.Key)).ToList();
				}
				shared[scene] = (common ?? new List<CatalogEntry>())
					.Where(e => e.Dlc < 0 || dlc.Contains(e.Dlc.ToString())).ToList();
			}
		}

		public static IEnumerable<string> Describe()
		{
			yield return $"Catalog ({byClient.Count} reports): {string.Join(", ", shared.OrderBy(s => s.Key).Select(s => $"{s.Key} {s.Value.Count} configs / {s.Value.Select(e => e.CarId).Distinct().Count()} models"))}";
			foreach (var report in byClient.OrderBy(r => r.Key))
				yield return $"  player {report.Key}: {string.Join(", ", report.Value.OrderBy(s => s.Key).Select(s => $"{s.Key} {s.Value.Count}"))}";
		}
	}
}
