using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Outdoor;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Outdoor
{
	public class SelectionHistory
	{
		public const int VisitsKept = 3;

		private readonly Dictionary<OutdoorCatalogScene, List<List<string>>> visits = new Dictionary<OutdoorCatalogScene, List<List<string>>>();

		public IReadOnlyList<string> LastVisit(OutdoorCatalogScene scene) =>
			visits.TryGetValue(scene, out var list) && list.Count > 0 ? list[list.Count - 1] : (IReadOnlyList<string>)new List<string>();

		public IReadOnlyList<IReadOnlyList<string>> Visits(OutdoorCatalogScene scene) =>
			visits.TryGetValue(scene, out var list) ? list.Cast<IReadOnlyList<string>>().ToList() : new List<IReadOnlyList<string>>();

		public void Record(OutdoorCatalogScene scene, IEnumerable<string> models)
		{
			if (!visits.TryGetValue(scene, out var list)) visits[scene] = list = new List<List<string>>();
			list.Add((models ?? Enumerable.Empty<string>()).Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList());
			while (list.Count > VisitsKept) list.RemoveAt(0);
		}

		public void TrimLast(OutdoorCatalogScene scene, IReadOnlyList<string> usedModels)
		{
			if (!visits.TryGetValue(scene, out var list) || list.Count == 0) return;
			var used = new HashSet<string>(usedModels ?? new List<string>());
			list[list.Count - 1] = list[list.Count - 1].Where(used.Contains).ToList();
		}

		public void Reset() => visits.Clear();

		public JToken Save()
		{
			var data = new JObject();
			foreach (var scene in visits.OrderBy(v => v.Key))
				data[scene.Key.ToString()] = new JArray(scene.Value.Select(v => new JArray(v)));
			return data;
		}

		public void Load(JToken data)
		{
			visits.Clear();
			if (!(data is JObject scenes)) return;
			foreach (var property in scenes.Properties())
			{
				if (!System.Enum.TryParse(property.Name, out OutdoorCatalogScene scene) || !(property.Value is JArray list)) continue;
				visits[scene] = list.OfType<JArray>().Select(v => v.Values<string>().ToList()).Skip(Math.Max(0, list.Count - VisitsKept)).ToList();
			}
		}

		public IEnumerable<string> Describe() =>
			visits.OrderBy(v => v.Key).Select(v => $"{v.Key}: {string.Join(" | ", v.Value.Select(models => models.Count == 0 ? "-" : string.Join(" ", models)))}");
	}
}
