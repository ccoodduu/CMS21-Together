using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Outdoor;

public static class OutdoorDigest
{
	public static List<string> LastRows { get; private set; } = new List<string>();

	public static void Reset() => LastRows = new List<string>();

	public static List<string> Build()
	{
		var rows = new List<string>();
		foreach (int index in OutdoorCarSync.CreatedIndices) rows.Add(CarRow(index));
		if (OutdoorSession.Scene == GameScene.Barn) rows.Add(BarnLayoutRow());
		return rows.Where(r => r != null).ToList();
	}

	private static string CarRow(int index)
	{
		var loader = OutdoorCarSync.LoaderAt(index);
		if (loader == null) return null;
		float conditionSum = 0f;
		int missing = 0;
		try
		{
			var registry = PartRegistry.Build(loader);
			var body = new List<CarBodyPartUpdatePacket>();
			var sub = new List<CarSubPartUpdatePacket>();
			CarPartsSync.CaptureAll(loader, registry, body, sub);
			conditionSum = body.Sum(b => b.State?.Condition ?? 0f) + sub.Sum(s => s.Condition);
			missing = body.Count(b => b.Unmounted);
		}
		catch (Exception ex)
		{
			Log.Debug($"[Outdoor] Digest of car {index}: parts not read ({ex.Message}).");
		}
		var color = loader.color;
		return OutdoorDigestRow.Car(index, new Dictionary<string, string>
		{
			[OutdoorDigestRow.CarIdField] = loader.carToLoad,
			["version"] = loader.ConfigVersion.ToString(CultureInfo.InvariantCulture),
			["pos"] = LootSync.Key(loader.transform.position),
			["colour"] = $"{Byte(color.r):x2}{Byte(color.g):x2}{Byte(color.b):x2}",
			["missing"] = missing.ToString(CultureInfo.InvariantCulture),
			["cond"] = conditionSum.ToString("0.0", CultureInfo.InvariantCulture),
			["neg"] = loader.negotationMod.ToString("0.000", CultureInfo.InvariantCulture),
		});
	}

	private static int Byte(float channel) => Mathf.Clamp(Mathf.RoundToInt(channel * 255f), 0, 255);

	private static string BarnLayoutRow()
	{
		var shed = UnityEngine.Object.FindObjectOfType<ShedManager>();
		var root = shed == null ? null : shed.ShedRoot;
		if (root == null) return null;
		var parts = new List<string>();
		foreach (var child in root.GetComponentsInChildren<Transform>(true))
		{
			if (child == null || child.GetComponent<CarLoader>() != null) continue;
			parts.Add($"{child.name}@{LootSync.Key(child.position)}");
		}
		parts.Sort(StringComparer.Ordinal);
		return OutdoorDigestRow.Build(OutdoorDigestRow.LayoutPrefix + "shed", new Dictionary<string, string>
		{
			["rot"] = root.transform.eulerAngles.y.ToString("0.0", CultureInfo.InvariantCulture),
			["parts"] = parts.Count.ToString(CultureInfo.InvariantCulture),
			["hash"] = Fnv(string.Join(";", parts)).ToString("x8"),
		});
	}

	private static uint Fnv(string text)
	{
		unchecked
		{
			uint hash = 2166136261;
			foreach (char c in text)
			{
				hash ^= c;
				hash *= 16777619;
			}
			return hash;
		}
	}

	public static void Send()
	{
		if (!OutdoorSession.IsShared) return;
		LastRows = Build();
		Client.Instance.Send(new OutdoorDigestPacket { InstanceId = OutdoorSession.InstanceId, Rows = LastRows });
		Log.Info($"[Outdoor] Digest sent: {LastRows.Count} rows.");
	}
}
