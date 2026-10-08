using System;
using CMS21Together.Logic.Seeding;
using CMS21Together.Network;

namespace CMS21Together.Logic.Outdoor;

public static class Reseed
{
	public static int Steps { get; private set; }

	public static bool Active => OutdoorSession.IsShared && Client.Instance != null && Client.Instance.IsConnectionValid;

	public static bool Begin(IntPtr iterator, string kind, int carIndex)
	{
		if (!Active) return false;
		Steps++;
		return SeededStreams.Begin(iterator, OutdoorSession.Seed, kind, carIndex);
	}

	public static void End(bool begun, IntPtr iterator, bool finished) => SeededStreams.End(begun, iterator, finished);

	public static void Reset()
	{
		SeededStreams.Reset();
		Steps = 0;
	}
}
