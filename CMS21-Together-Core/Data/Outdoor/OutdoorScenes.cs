using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Enum;

namespace CMS21_Together_Core.Data.Outdoor;

public static class OutdoorScenes
{
	public static readonly GameScene[] All = { GameScene.Junkyard, GameScene.Barn, GameScene.Auction };

	public const string DefaultSetting = "junkyard,barn,auction";

	public static bool IsOutdoor(GameScene scene) => Array.IndexOf(All, scene) >= 0;

	public static bool HasPiles(GameScene scene) => scene == GameScene.Junkyard || scene == GameScene.Barn;

	public static List<GameScene> Parse(string value, Action<string> unknown = null)
	{
		var scenes = new List<GameScene>();
		foreach (string part in (value ?? "").Replace("\"", "").Split(','))
		{
			string name = part.Trim();
			if (name.Length == 0) continue;
			if (System.Enum.TryParse(name, true, out GameScene scene) && IsOutdoor(scene))
			{
				if (!scenes.Contains(scene)) scenes.Add(scene);
			}
			else unknown?.Invoke(name);
		}
		return scenes.OrderBy(s => (int)s).ToList();
	}

	public static string Format(IEnumerable<GameScene> scenes)
	{
		var list = (scenes ?? Enumerable.Empty<GameScene>()).Select(s => s.ToString().ToLowerInvariant()).ToList();
		return list.Count == 0 ? "none" : string.Join(",", list);
	}

	public static OutdoorCatalogScene[] CatalogScenesFor(GameScene scene)
	{
		switch (scene)
		{
			case GameScene.Junkyard: return new[] { OutdoorCatalogScene.Junkyard };
			case GameScene.Barn: return new[] { OutdoorCatalogScene.Barn };
			case GameScene.Auction: return new[] { OutdoorCatalogScene.AuctionNormal, OutdoorCatalogScene.AuctionSalvage };
			default: return new OutdoorCatalogScene[0];
		}
	}

	public static OutdoorCatalogScene CatalogSceneFor(AuctionKind kind) =>
		kind == AuctionKind.Normal ? OutdoorCatalogScene.AuctionNormal : OutdoorCatalogScene.AuctionSalvage;
}
