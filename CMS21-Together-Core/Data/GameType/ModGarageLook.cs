using System;
using System.Linq;

namespace CMS21_Together_Core.Data.GameType;

[Serializable]
public class ModGarageLook
{
	public const int DefaultMaterial = -1;
	public const int MaxSections = 64;
	public const int MaxMaterialIndex = 63;

	public int[] MaterialIndexes = new int[0];
	public string TexturePack;
	public int SectionCount;

	public int MaterialAt(int section) =>
		MaterialIndexes != null && section >= 0 && section < MaterialIndexes.Length ? MaterialIndexes[section] : DefaultMaterial;

	public ModGarageLook Clamped()
	{
		var indexes = (MaterialIndexes ?? new int[0]).Take(MaxSections)
			.Select(i => Math.Max(DefaultMaterial, Math.Min(MaxMaterialIndex, i))).ToArray();
		return new ModGarageLook
		{
			MaterialIndexes = indexes,
			TexturePack = string.IsNullOrEmpty(TexturePack) ? null : TexturePack,
			SectionCount = Math.Max(0, Math.Min(MaxSections, SectionCount)),
		};
	}

	public bool SameLook(ModGarageLook other)
	{
		if (other == null) return false;
		if ((string.IsNullOrEmpty(TexturePack) ? null : TexturePack) != (string.IsNullOrEmpty(other.TexturePack) ? null : other.TexturePack)) return false;
		int length = Math.Max(MaterialIndexes?.Length ?? 0, other.MaterialIndexes?.Length ?? 0);
		for (int i = 0; i < length; i++)
			if (MaterialAt(i) != other.MaterialAt(i)) return false;
		return true;
	}

	public string Describe()
	{
		var changed = (MaterialIndexes ?? new int[0]).Select((m, i) => (m, i)).Where(p => p.m != DefaultMaterial).Select(p => $"{p.i}={p.m}").ToList();
		return $"{(changed.Count == 0 ? "all default" : string.Join(" ", changed))}, pack {TexturePack ?? "default"}, {SectionCount} sections";
	}
}
