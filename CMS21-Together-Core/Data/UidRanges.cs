namespace CMS21_Together_Core.Data;

public static class UidRanges
{
	public const long Size = 1_000_000_000_000L;

	public static long Start(int playerId) => playerId * Size;

	public static bool Contains(int playerId, long uid) => playerId > 0 && uid >= Start(playerId) && uid < Start(playerId) + Size;
}
