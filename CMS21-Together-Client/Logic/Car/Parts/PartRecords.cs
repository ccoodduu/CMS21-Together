using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;

namespace CMS21Together.Logic.Car.Parts;

public static class PartRecords
{
	public static CarBodyPartUpdatePacket Capture(int index, CarPart part) => new CarBodyPartUpdatePacket
	{
		PartIndex = index,
		PartName = part.name,
		Switched = part.Switched,
		Unmounted = part.Unmounted,
		TunedID = part.TunedID,
		State = new ModItem { ID = part.name, Condition = part.Condition, Dent = part.Dent, Quality = part.Quality }
	};

	public static CarSubPartUpdatePacket Capture(int[] path, PartScript script)
	{
		var mountData = script.GetMountObjectDataForSave();
		return new CarSubPartUpdatePacket
		{
			PartIndexPath = path,
			PartId = script.id,
			TunedID = script.tunedID,
			Unmounted = script.IsUnmounted,
			Condition = script.Condition,
			Quality = script.Quality,
			IsExamined = script.IsExamined,
			IsPainted = script.IsPainted,
			Dust = script.Dust,
			MountObjectData = mountData == null ? null : new ModMountObjectData
			{
				ParentPath = mountData.ParentPath,
				Condition = mountData.Condition?.ToArray(),
				IsStuck = mountData.IsStuck?.ToArray()
			}
		};
	}

	public static bool SameState(CarBodyPartUpdatePacket a, CarBodyPartUpdatePacket b) =>
		a != null && b != null && a.Unmounted == b.Unmounted && a.Switched == b.Switched && a.TunedID == b.TunedID
		&& Close(a.State?.Condition, b.State?.Condition) && Close(a.State?.Dent, b.State?.Dent) && a.State?.Quality == b.State?.Quality;

	public static bool SameState(CarSubPartUpdatePacket a, CarSubPartUpdatePacket b) =>
		a != null && b != null && a.Unmounted == b.Unmounted && a.EffectiveId == b.EffectiveId && a.IsExamined == b.IsExamined
		&& Close(a.Condition, b.Condition) && a.Quality == b.Quality && Close(a.Dust, b.Dust);

	private static bool Close(float? a, float? b) => a.HasValue == b.HasValue && (!a.HasValue || System.Math.Abs(a.Value - b.Value) < 0.001f);
}
