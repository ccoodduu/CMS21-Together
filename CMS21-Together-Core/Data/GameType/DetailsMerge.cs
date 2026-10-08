using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Core.Data.GameType;

public static class DetailsMerge
{
	public const int AllWheels = 0;
	public const AlignmentFields AllAlignment = AlignmentFields.None;

	public static bool HasWheel(int wheelMask, int index) => wheelMask == AllWheels || (wheelMask & (1 << index)) != 0;

	public static bool HasAlignment(AlignmentFields mask, int field) => mask == AllAlignment || ((int)mask & (1 << field)) != 0;

	public static void Merge(ModCarDetails stored, ModCarDetails incoming, int wheelMask, AlignmentFields alignmentMask)
	{
		if (incoming.Fluids != null)
		{
			stored.Fluids ??= new List<ModFluidLevel>();
			foreach (var fluid in incoming.Fluids)
			{
				stored.Fluids.RemoveAll(f => f.Type == fluid.Type && f.Id == fluid.Id);
				stored.Fluids.Add(fluid);
			}
		}
		if (incoming.BodyCosmetics != null)
		{
			stored.BodyCosmetics ??= new List<ModBodyCosmetics>();
			foreach (var part in incoming.BodyCosmetics)
			{
				stored.BodyCosmetics.RemoveAll(p => p.PartIndex == part.PartIndex);
				stored.BodyCosmetics.Add(part);
			}
		}
		if (incoming.Tuning != null)
		{
			if (stored.Tuning == null) stored.Tuning = incoming.Tuning;
			else
			{
				if (incoming.Tuning.Gearbox != null)
				{
					stored.Tuning.Gearbox = incoming.Tuning.Gearbox;
					stored.Tuning.GearboxPartKey = incoming.Tuning.GearboxPartKey;
				}
				foreach (var module in incoming.Tuning.Modules ?? new List<ModPartTuning>())
				{
					stored.Tuning.Modules.RemoveAll(m => m.PartKey == module.PartKey);
					stored.Tuning.Modules.Add(module);
				}
			}
		}
		if (incoming.Wheels != null)
		{
			if (stored.Wheels == null || wheelMask == AllWheels) stored.Wheels = incoming.Wheels;
			else
			{
				if (stored.Wheels.Length < incoming.Wheels.Length)
				{
					var grown = new ModCarWheel[incoming.Wheels.Length];
					Array.Copy(stored.Wheels, grown, stored.Wheels.Length);
					stored.Wheels = grown;
				}
				for (int i = 0; i < incoming.Wheels.Length; i++)
					if (HasWheel(wheelMask, i)) stored.Wheels[i] = incoming.Wheels[i];
			}
		}
		if (incoming.Alignment != null)
		{
			if (stored.Alignment == null || alignmentMask == AllAlignment) stored.Alignment = incoming.Alignment;
			else
				for (int field = 0; field < CarDetailEntries.AlignmentFieldNames.Length; field++)
					if (HasAlignment(alignmentMask, field))
						CarDetailEntries.SetAlignmentValue(stored.Alignment, field, CarDetailEntries.AlignmentValue(incoming.Alignment, field));
		}
		if (incoming.Paint != null) stored.Paint = incoming.Paint;
		if (incoming.Plates != null) stored.Plates = incoming.Plates;
		if (incoming.Info != null) stored.Info = incoming.Info;
		if (incoming.BonusParts != null) stored.BonusParts = incoming.BonusParts;
		if (incoming.Dyno != null) stored.Dyno = incoming.Dyno;
	}

	public static SortedDictionary<string, string> CarriedSignatures(ModCarDetails details, int wheelMask, AlignmentFields alignmentMask)
	{
		var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
		foreach (var pair in CarDetailEntries.Signatures(details))
			if (Carries(pair.Key, wheelMask, alignmentMask)) result[pair.Key] = pair.Value;
		return result;
	}

	public static bool Carries(string entry, int wheelMask, AlignmentFields alignmentMask)
	{
		if (entry.StartsWith("w:")) return HasWheel(wheelMask, int.Parse(entry.Substring(2)));
		if (entry.StartsWith("a:")) return HasAlignment(alignmentMask, Array.IndexOf(CarDetailEntries.AlignmentFieldNames, entry.Substring(2)));
		return true;
	}

	public static (ModCarDetails Details, int WheelMask, AlignmentFields AlignmentMask) Only(ModCarDetails details, int wheelMask, AlignmentFields alignmentMask, ICollection<string> keep)
	{
		var result = new ModCarDetails { SpawnSeq = details.SpawnSeq, HasSnapshot = details.HasSnapshot };
		if (details.Fluids != null)
		{
			var fluids = details.Fluids.Where(f => keep.Contains(CarDetailEntries.Fluid(f.Type, f.Id))).ToList();
			if (fluids.Count > 0) result.Fluids = fluids;
		}
		int wheels = 0;
		if (details.Wheels != null)
		{
			for (int i = 0; i < details.Wheels.Length; i++)
				if (HasWheel(wheelMask, i) && keep.Contains(CarDetailEntries.Wheel(i))) wheels |= 1 << i;
			if (wheels != 0) result.Wheels = details.Wheels;
		}
		int alignment = 0;
		if (details.Alignment != null)
		{
			for (int field = 0; field < CarDetailEntries.AlignmentFieldNames.Length; field++)
				if (HasAlignment(alignmentMask, field) && keep.Contains(CarDetailEntries.Alignment(CarDetailEntries.AlignmentFieldNames[field]))) alignment |= 1 << field;
			if (alignment != 0) result.Alignment = details.Alignment;
		}
		if (details.BodyCosmetics != null)
		{
			var panels = details.BodyCosmetics.Where(p => keep.Contains(CarDetailEntries.Cosmetics(p.PartIndex))).ToList();
			if (panels.Count > 0) result.BodyCosmetics = panels;
		}
		if (details.Tuning != null)
		{
			var tuning = new ModCarTuning
			{
				Gearbox = details.Tuning.Gearbox != null && keep.Contains(CarDetailEntries.Gearbox) ? details.Tuning.Gearbox : null,
				GearboxPartKey = details.Tuning.GearboxPartKey,
				Modules = (details.Tuning.Modules ?? new List<ModPartTuning>()).Where(m => keep.Contains(CarDetailEntries.Module(m.PartKey))).ToList(),
			};
			if (tuning.Gearbox != null || tuning.Modules.Count > 0) result.Tuning = tuning;
		}
		if (keep.Contains(CarDetailEntries.Paint)) result.Paint = details.Paint;
		if (keep.Contains(CarDetailEntries.Plates)) result.Plates = details.Plates;
		if (keep.Contains(CarDetailEntries.Info)) result.Info = details.Info;
		if (keep.Contains(CarDetailEntries.Dyno)) result.Dyno = details.Dyno;
		return (result, wheels, (AlignmentFields)alignment);
	}

	public static bool IsEmpty(ModCarDetails details) =>
		details.Fluids == null && details.Wheels == null && details.Alignment == null && details.BodyCosmetics == null && details.Tuning == null
		&& details.Paint == null && details.Plates == null && details.Info == null && details.Dyno == null && details.BonusParts == null;
}
