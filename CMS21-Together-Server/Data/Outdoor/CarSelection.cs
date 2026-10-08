using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Data.Outdoor
{
	public class SelectionRequest
	{
		public OutdoorCatalogScene Scene;
		public int Count;
		public IReadOnlyList<CatalogEntry> Candidates;
		public SelectionHistory History;
		public Random Random;
	}

	public interface ICarSelector
	{
		string Name { get; }
		IReadOnlyList<OutdoorCarPick> Select(SelectionRequest request);
	}

	public class BasicCarSelector : ICarSelector
	{
		public string Name => "basic";

		public IReadOnlyList<OutdoorCarPick> Select(SelectionRequest request)
		{
			var picks = new List<OutdoorCarPick>();
			var candidates = request.Candidates ?? new List<CatalogEntry>();
			if (request.Count <= 0 || candidates.Count == 0) return picks;

			var configs = candidates.GroupBy(c => c.CarId).ToDictionary(g => g.Key, g => g.ToList());
			var models = configs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
			var lastVisit = new HashSet<string>(request.History?.LastVisit(request.Scene) ?? new List<string>());
			var fresh = Shuffled(models.Where(m => !lastVisit.Contains(m)), request.Random);
			var recent = Shuffled(models.Where(m => lastVisit.Contains(m)), request.Random);

			foreach (string model in fresh.Concat(recent).Take(request.Count))
				picks.Add(Pick(configs[model], request.Random));
			while (picks.Count < request.Count)
				picks.Add(Pick(configs[models[request.Random.Next(models.Count)]], request.Random));
			return picks;
		}

		private static OutdoorCarPick Pick(List<CatalogEntry> configs, Random random)
		{
			var entry = configs[random.Next(configs.Count)];
			return new OutdoorCarPick { CarId = entry.CarId, ConfigVersion = entry.ConfigVersion, Dlc = entry.Dlc };
		}

		private static List<string> Shuffled(IEnumerable<string> source, Random random)
		{
			var list = source.ToList();
			for (int i = list.Count - 1; i > 0; i--)
			{
				int j = random.Next(i + 1);
				(list[i], list[j]) = (list[j], list[i]);
			}
			return list;
		}
	}

	public static class CarSelectors
	{
		public const string Default = "basic";

		public static ICarSelector Create(string name)
		{
			switch ((name ?? "").Trim().ToLowerInvariant())
			{
				case "":
				case Default:
					return new BasicCarSelector();
				default:
					Logger.Warn($"[Outdoor] Unknown car_selector '{name}'; using '{Default}'.");
					return new BasicCarSelector();
			}
		}
	}
}
