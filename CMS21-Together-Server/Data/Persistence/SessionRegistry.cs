using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Data.Persistence
{
	public static class SessionRegistry
	{
		public static IReadOnlyList<ISaveSection> SaveSections { get; private set; } = new List<ISaveSection>();
		public static IReadOnlyList<ISnapshotProvider> SnapshotProviders { get; private set; } = new List<ISnapshotProvider>();

		public static void Initialize(Assembly assembly)
		{
			var sections = new Dictionary<string, ISaveSection>();
			var providers = new Dictionary<string, ISnapshotProvider>();

			var types = assembly.GetTypes()
				.Where(t => t.GetCustomAttribute<SessionSectionAttribute>() != null)
				.OrderBy(t => t.FullName);

			foreach (var type in types)
			{
				object instance = Activator.CreateInstance(type);

				if (instance is ISaveSection section)
				{
					if (sections.ContainsKey(section.Key))
						throw new InvalidOperationException($"Duplicate save section key '{section.Key}' ({sections[section.Key].GetType().Name}, {type.Name}).");
					sections.Add(section.Key, section);
				}

				if (instance is ISnapshotProvider provider)
				{
					if (providers.ContainsKey(provider.Key))
						throw new InvalidOperationException($"Duplicate snapshot provider key '{provider.Key}' ({providers[provider.Key].GetType().Name}, {type.Name}).");
					providers.Add(provider.Key, provider);
				}

				if (!(instance is ISaveSection) && !(instance is ISnapshotProvider))
					throw new InvalidOperationException($"{type.Name} has [SessionSection] but implements neither ISaveSection nor ISnapshotProvider.");
			}

			SaveSections = sections.Values.OrderBy(s => s.Key, StringComparer.Ordinal).ToList();
			SnapshotProviders = providers.Values.OrderBy(p => p.SyncOrder).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();

			Logger.Info($"[SessionRegistry] Save sections: {string.Join(", ", SaveSections.Select(s => $"{s.Key} v{s.Version}"))}");
			Logger.Info($"[SessionRegistry] Snapshot providers: {string.Join(", ", SnapshotProviders.Select(p => $"{p.SyncOrder}:{p.Key}"))}");
		}
	}
}
