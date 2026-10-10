using System.Diagnostics;

namespace CMS21Together.Network;

public static class PacketClock
{
	private static readonly Stopwatch clock = Stopwatch.StartNew();

	public static long NowMs => clock.ElapsedMilliseconds;

	public static long ReceivedMs { get; private set; }

	public static void Dispatching(long receivedMs) => ReceivedMs = receivedMs;
}
