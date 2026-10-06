using System;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Server.Data.Economy
{
	// Ports of GlobalData.GetScrapFromItem, GlobalData.GetCostForScrapUpgrade and the per-condition filter of
	// Inventory.ScrapPerCondition (spike economy-paths, decompile of 2026-10-06).
	public static class ScrapFormulas
	{
		public const int MaxQuality = 3;

		public static bool TryGetProperty(string itemId, out PartProperty property)
		{
			property = null;
			return itemId != null && GameDatabase.ItemsDatabase != null && GameDatabase.ItemsDatabase.TryGetValue(itemId, out property);
		}

		public static int? ScrapFromItem(ModItem item, int scrapType)
		{
			if (item == null || !TryGetProperty(item.ID, out var property)) return null;
			float value = Math.Min(item.Condition, item.Dent) * property.Price;
			float quality = item.Quality + 1;
			switch (scrapType)
			{
				case 0: return Convert.ToInt32(quality * value * 0.001f) + 1;
				case 1: return Convert.ToInt32(quality * 1.2f * value * 0.004f) + 2;
				case 2: return Convert.ToInt32(quality * 1.5f * value * 0.006f) + 3;
				default: return 1;
			}
		}

		public static int UpgradeCost(int targetQuality, int itemValue)
		{
			switch (targetQuality)
			{
				case 1: return (int)Math.Floor(itemValue * 0.03f + 10f);
				case 2: return (int)Math.Floor(itemValue * 0.07f + 15f);
				case 3: return (int)Math.Floor(itemValue * 0.14f + 35f);
				default: return 10;
			}
		}

		public static int UpgradeItemValue(ModItem item, int targetQuality)
		{
			int quality = item.Quality;
			item.Quality = targetQuality;
			try
			{
				return PricingCalculator.GetPrice(item, 0.5f);
			}
			finally
			{
				item.Quality = quality;
			}
		}

		public static bool ScrapsPerCondition(ModItem item, float sliderValue)
		{
			if (!TryGetProperty(item.ID, out var property)) return false;
			if (property.SpecialGroup == SpecialGroup.SpecialCase || property.SpecialGroup == SpecialGroup.SpecialMap) return false;
			return RoundCondition(Math.Min(item.Condition, item.Dent)) <= sliderValue;
		}

		public static int RoundCondition(float condition) => (int)Math.Round(condition * 100f);
	}
}
