using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CMS21_Together_Core.Data.Digest;

[Serializable]
public class ProjectionRow
{
	public string Id;
	public string Field;
	public string Value;
}

[Serializable]
public class Projection
{
	public List<ProjectionRow> Rows = new List<ProjectionRow>();

	public Projection Add(string id, string field, string value)
	{
		Rows.Add(new ProjectionRow { Id = id, Field = field, Value = value ?? "" });
		return this;
	}

	public Projection Add(string id, string field, float value) => Add(id, field, Math.Round(value, 3).ToString("0.000", CultureInfo.InvariantCulture));

	public Projection Add(string id, string field, int value) => Add(id, field, value.ToString(CultureInfo.InvariantCulture));

	public Projection Add(string id, string field, bool value) => Add(id, field, value ? "1" : "0");

	public IEnumerable<ProjectionRow> Sorted() =>
		Rows.OrderBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.Field, StringComparer.Ordinal);

	public ulong Hash() => CanonicalHasher.Hash(this);

	public static List<(ProjectionRow Client, ProjectionRow Server)> Diff(Projection client, Projection server)
	{
		var clientRows = client.Rows.GroupBy(r => r.Id + "|" + r.Field).ToDictionary(g => g.Key, g => g.First());
		var serverRows = server.Rows.GroupBy(r => r.Id + "|" + r.Field).ToDictionary(g => g.Key, g => g.First());
		var diff = new List<(ProjectionRow, ProjectionRow)>();
		foreach (string key in clientRows.Keys.Union(serverRows.Keys).OrderBy(k => k, StringComparer.Ordinal))
		{
			clientRows.TryGetValue(key, out var c);
			serverRows.TryGetValue(key, out var s);
			if (c?.Value != s?.Value) diff.Add((c, s));
		}
		return diff;
	}
}

public static class CanonicalHasher
{
	private const ulong OffsetBasis = 14695981039346656037UL;
	private const ulong Prime = 1099511628211UL;

	public static ulong Hash(Projection projection)
	{
		ulong hash = OffsetBasis;
		foreach (var row in projection.Sorted())
			foreach (byte b in Encoding.UTF8.GetBytes($"{row.Id}|{row.Field}={row.Value}\n"))
			{
				hash ^= b;
				hash *= Prime;
			}
		return hash;
	}
}
