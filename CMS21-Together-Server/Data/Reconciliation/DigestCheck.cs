using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Digest;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Server.Data.Reconciliation
{
	// --check-digest: the canonical hash ignores list order and float noise below 0.0005, and changes with any field.
	public static class DigestCheck
	{
		public static int Run()
		{
			var items = new List<ModItem>
			{
				new ModItem { UID = 1, ID = "door_1", Condition = 0.5f, Quality = 1 },
				new ModItem { UID = 2, ID = "hood_1", Condition = 0.25f, Quality = 2 },
			};
			var shuffled = items.AsEnumerable().Reverse().Select(i => new ModItem { UID = i.UID, ID = i.ID, Condition = i.Condition + 0.0004f, Quality = i.Quality }).ToList();
			var changed = items.Select(i => new ModItem { UID = i.UID, ID = i.ID, Condition = i.Condition, Quality = i.Quality }).ToList();
			changed[1].Quality = 3;

			ulong original = DigestMappers.Inventory(items, null, null, null).Hash();
			ulong reordered = DigestMappers.Inventory(shuffled, null, null, null).Hash();
			ulong different = DigestMappers.Inventory(changed, null, null, null).Hash();

			bool ok = original == reordered && original != different;
			Console.WriteLine($"digest check: original {original:X16}, reordered + noise {reordered:X16}, changed field {different:X16} -> {(ok ? "OK" : "FAILED")}");
			return ok ? 0 : 1;
		}
	}
}
