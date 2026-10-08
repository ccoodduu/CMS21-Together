using System;
using System.Collections.Generic;
using System.Linq;

namespace CMS21_Together_Core.Data;

[Serializable]
public class ShopListEntry
{
	public string Id;
	public int Amount;
	public string LicensePlateName;
	public bool LicensePlate;
	public bool Tire;
	public bool Rim;
	public int Width;
	public int Size;
	public int Profile;
	public int ET;

	public bool SameItem(ShopListEntry other) =>
		other != null && string.Equals(Id, other.Id, StringComparison.Ordinal) &&
		string.Equals(LicensePlateName, other.LicensePlateName, StringComparison.Ordinal) && LicensePlate == other.LicensePlate &&
		Tire == other.Tire && Rim == other.Rim && Width == other.Width && Size == other.Size && Profile == other.Profile && ET == other.ET;

	public ShopListEntry WithAmount(int amount)
	{
		var copy = (ShopListEntry)MemberwiseClone();
		copy.Amount = amount;
		return copy;
	}

	public string Describe()
	{
		string extra = LicensePlate ? $" plate '{LicensePlateName}'" : Tire || Rim ? $" {(Tire ? "tire" : "rim")} {Width}/{Size}/{Profile} ET{ET}" : "";
		return $"{Id}{extra} x{Amount}";
	}

	public static bool SameLists(IList<ShopListEntry> left, IList<ShopListEntry> right) =>
		left != null && right != null && left.Count == right.Count && left.Zip(right, (l, r) => l.SameItem(r) && l.Amount == r.Amount).All(same => same);
}

public class ShopListState
{
	public const int MaxAmount = 99;
	public const int MaxEntries = 500;

	public List<ShopListEntry> Entries = new List<ShopListEntry>();
	public int Revision;
}
