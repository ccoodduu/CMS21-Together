namespace CMS21_Together_Core.Data;

public static class ParkingLayout
{
	public const int SlotsPerLevel = 10;
	public const int MaxLevels = 80;
	public const int DefaultUnlockedLevels = 1;

	public static int UsableSlots(int unlockedLevels) => unlockedLevels * SlotsPerLevel;
}
