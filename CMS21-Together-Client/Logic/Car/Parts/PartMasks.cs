using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;

namespace CMS21Together.Logic.Car.Parts;

public static class PartMasks
{
	private const PartFields SubFallback = PartFields.Condition | PartFields.Examined | PartFields.Dust;
	private const PartFields BodyFallback = PartFields.Condition;

	public static PartFields For(CarSubPartUpdatePacket record, CarSubPartUpdatePacket last)
	{
		if (last == null) return PartFields.All;
		var fields = PartRecordMerge.Differ(record, last);
		return fields != PartFields.None ? fields : SubFallback;
	}

	public static PartFields For(CarBodyPartUpdatePacket record, CarBodyPartUpdatePacket last)
	{
		if (last == null) return PartFields.All;
		var fields = PartRecordMerge.Differ(record, last);
		return fields != PartFields.None ? fields : BodyFallback;
	}

	public static string Describe(IEnumerable<CarBodyPartUpdatePacket> body, IEnumerable<CarSubPartUpdatePacket> sub) =>
		string.Join("; ", body.Select(r => r.Changed).Concat(sub.Select(r => r.Changed))
			.GroupBy(c => c).Select(g => $"{g.Key.ToString().Replace(", ", "|")} x{g.Count()}"));
}
