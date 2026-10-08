using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CMS21_Together_Core.Data.GameType;

public static class CarDetailEntries
{
	public static readonly string[] AlignmentFieldNames = { "FL", "FR", "RL", "RR", "LampLH", "LampLV", "LampRH", "LampRV" };

	public const string Gearbox = "t:gearbox";
	public const string Paint = "paint";
	public const string Plates = "plates";
	public const string Info = "info";
	public const string Dyno = "dyno";

	public static string Fluid(ModCarFluidType type, int id) => $"f:{type}.{id}";

	public static string Wheel(int index) => $"w:{index}";

	public static string Alignment(string field) => $"a:{field}";

	public static string Cosmetics(int partIndex) => $"c:{partIndex}";

	public static string Module(string partKey) => $"t:{partKey}";

	public static CarDetailSection SectionOf(string entry)
	{
		if (entry.StartsWith("f:")) return CarDetailSection.Fluids;
		if (entry.StartsWith("w:")) return CarDetailSection.Wheels;
		if (entry.StartsWith("a:")) return CarDetailSection.Alignment;
		if (entry.StartsWith("c:")) return CarDetailSection.BodyCosmetics;
		if (entry.StartsWith("t:")) return CarDetailSection.Tuning;
		return entry switch
		{
			Paint => CarDetailSection.Paint,
			Plates => CarDetailSection.Plates,
			Info => CarDetailSection.Info,
			Dyno => CarDetailSection.Dyno,
			_ => CarDetailSection.None,
		};
	}

	public static float AlignmentValue(ModAlignment alignment, int field) => field switch
	{
		0 => alignment.FL, 1 => alignment.FR, 2 => alignment.RL, 3 => alignment.RR,
		4 => alignment.LampLH, 5 => alignment.LampLV, 6 => alignment.LampRH, _ => alignment.LampRV,
	};

	public static void SetAlignmentValue(ModAlignment alignment, int field, float value)
	{
		switch (field)
		{
			case 0: alignment.FL = value; break;
			case 1: alignment.FR = value; break;
			case 2: alignment.RL = value; break;
			case 3: alignment.RR = value; break;
			case 4: alignment.LampLH = value; break;
			case 5: alignment.LampLV = value; break;
			case 6: alignment.LampRH = value; break;
			default: alignment.LampRV = value; break;
		}
	}

	public static SortedDictionary<string, object> Split(ModCarDetails details)
	{
		var entries = new SortedDictionary<string, object>(StringComparer.Ordinal);
		if (details == null) return entries;
		if (details.Fluids != null)
			foreach (var fluid in details.Fluids)
				entries[Fluid(fluid.Type, fluid.Id)] = fluid;
		if (details.Wheels != null)
			for (int i = 0; i < details.Wheels.Length; i++)
				entries[Wheel(i)] = details.Wheels[i];
		if (details.Alignment != null)
			for (int i = 0; i < AlignmentFieldNames.Length; i++)
				entries[Alignment(AlignmentFieldNames[i])] = AlignmentValue(details.Alignment, i);
		if (details.BodyCosmetics != null)
			foreach (var panel in details.BodyCosmetics)
				entries[Cosmetics(panel.PartIndex)] = panel;
		if (details.Tuning != null)
		{
			if (details.Tuning.Gearbox != null) entries[Gearbox] = details.Tuning.Gearbox;
			if (details.Tuning.Modules != null)
				foreach (var module in details.Tuning.Modules)
					entries[Module(module.PartKey)] = module;
		}
		if (details.Paint != null) entries[Paint] = details.Paint;
		if (details.Plates != null) entries[Plates] = details.Plates;
		if (details.Info != null) entries[Info] = details.Info;
		if (details.Dyno != null) entries[Dyno] = details.Dyno;
		return entries;
	}

	public static SortedDictionary<string, string> Signatures(ModCarDetails details)
	{
		var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
		foreach (var pair in Split(details)) result[pair.Key] = Signature(pair.Value);
		return result;
	}

	private static readonly JsonSerializerSettings Rounded = new JsonSerializerSettings { FloatFormatHandling = FloatFormatHandling.String, Converters = { new RoundingConverter() } };

	public static string Signature(object value) => JsonConvert.SerializeObject(value, Rounded);

	private sealed class RoundingConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType) => objectType == typeof(float) || objectType == typeof(double);
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue(Math.Round(Convert.ToDouble(value), 3));
		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotSupportedException();
		public override bool CanRead => false;
	}
}
