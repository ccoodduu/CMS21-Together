using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace CMS21_Together_Server.Diagnostics.Perf
{
	public static class PerfSummary
	{
		private const double RateWindowSeconds = 10;

		private static readonly TrafficSnapshot baseline = new TrafficSnapshot();
		private static readonly TrafficSnapshot windowStart = new TrafficSnapshot();
		private static readonly TrafficSnapshot windowEnd = new TrafficSnapshot();
		private static readonly TrafficSnapshot current = new TrafficSnapshot();
		private static readonly TimingSnapshot timingBaseline = new TimingSnapshot();
		private static readonly TimingSnapshot timingCurrent = new TimingSnapshot();
		private static ProcessReading baselineProcess;
		private static double windowStartAt = -1;
		private static double windowEndAt;
		private static bool resetByCommand;

		public static void Reset(bool byCommand = false)
		{
			resetByCommand = byCommand;
			TrafficCounters.Capture(baseline);
			HandlerTimings.Capture(timingBaseline);
			HandlerTimings.ResetMax();
			baselineProcess = ProcessReading.Read();
			windowEnd.CopyFrom(baseline);
			windowEndAt = baselineProcess.At;
			windowStartAt = -1;
		}

		public static void Tick()
		{
			double now = PerfLog.Now;
			if (now - windowEndAt < RateWindowSeconds) return;
			windowStart.CopyFrom(windowEnd);
			windowStartAt = windowEndAt;
			TrafficCounters.Capture(windowEnd);
			windowEndAt = now;
		}

		public static List<string> Describe() => WithInvariantCulture(DescribeLines);

		public static List<string> DescribeTop(int count) => WithInvariantCulture(() => TopLines(count));

		private static List<string> WithInvariantCulture(Func<IEnumerable<string>> lines)
		{
			var culture = Thread.CurrentThread.CurrentCulture;
			Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
			try
			{
				return lines().ToList();
			}
			finally
			{
				Thread.CurrentThread.CurrentCulture = culture;
			}
		}

		private static IEnumerable<string> DescribeLines()
		{
			var process = ProcessReading.Read();
			TrafficCounters.Capture(current);
			HandlerTimings.Capture(timingCurrent);
			double elapsed = Math.Max(process.At - baselineProcess.At, 0.001);

			yield return $"Perf since {Since} ({FormatDuration(elapsed)}, uptime {FormatDuration(process.At)}):";
			yield return $"  CPU {process.CpuPercentSince(baselineProcess):0.0} % of one core, private {Mb(process.PrivateBytes)}, working set {Mb(process.WorkingSet)}, " +
			             $"managed heap {Mb(process.ManagedHeap)}, threads {process.Threads}, handles {process.Handles}, GC {process.Gc0}/{process.Gc1}/{process.Gc2}";
			var save = PerfLog.LastSave();
			yield return $"  Last save: {(save.Bytes < 0 ? "none" : $"{Kb(save.Bytes)} in {save.Ms:0} ms at {save.At:HH:mm:ss}")}; Log/ {Mb(PerfLog.LogDirectorySize())}, desync records {PerfLog.DesyncRecordCount()}";
			yield return "  Players (down = server to player, up = player to server; now = last 10 s):";

			bool hasWindow = windowStartAt >= 0;
			double window = Math.Max(windowEndAt - windowStartAt, 0.001);
			foreach (var client in Network.Server.Clients.Values)
			{
				long down = current.SlotTotal(baseline, client.ID, PerfDirection.Sent, true);
				long up = current.SlotTotal(baseline, client.ID, PerfDirection.Received, true);
				if (!client.IsConnected && down == 0 && up == 0) continue;
				string name = client.IsConnected ? Data.Presence.PresenceRegistry.Get(client.ID)?.Username ?? "?" : "left";
				string now = hasWindow
					? $" (now {Rate(windowEnd.SlotTotal(windowStart, client.ID, PerfDirection.Sent, true) / window)} / {Rate(windowEnd.SlotTotal(windowStart, client.ID, PerfDirection.Received, true) / window)})"
					: "";
				yield return $"    Client[{client.ID}] '{name}' {(client.IsConnected ? client.SyncState.ToString() : "")}: down {Rate(down / elapsed)}, up {Rate(up / elapsed)}{now}; total {Kb(down)} / {Kb(up)}";
			}
			long unassigned = current.SlotTotal(baseline, 0, PerfDirection.Sent, true) + current.SlotTotal(baseline, 0, PerfDirection.Received, true);
			if (unassigned > 0) yield return $"    unassigned connections: {Kb(unassigned)}";

			yield return "  Top packet types by bytes:";
			foreach (string line in TopTraffic(10)) yield return line;

			var worstRun = Worst(TimingPart.Run);
			var worstWait = Worst(TimingPart.LockWait);
			yield return worstRun.Key < 0
				? "  No handler timed yet."
				: $"  Slowest handler: {HandlerTimings.KeyName(worstRun.Key)} {worstRun.Ms:0.0} ms; longest lock wait: {HandlerTimings.KeyName(worstWait.Key)} {worstWait.Ms:0.0} ms";
		}

		private static IEnumerable<string> TopLines(int count)
		{
			TrafficCounters.Capture(current);
			HandlerTimings.Capture(timingCurrent);

			yield return $"Top {count} packet types by bytes since {Since}:";
			foreach (string line in TopTraffic(count)) yield return line;

			yield return $"Top {count} timings by total handler time (ms; wait = waiting for StateLock):";
			var keys = Enumerable.Range(0, HandlerTimings.KeyCount)
				.Where(k => timingCurrent.Count(timingBaseline, HandlerTimings.Index(k, TimingPart.Run)) > 0)
				.OrderByDescending(k => timingCurrent.TotalMs(timingBaseline, HandlerTimings.Index(k, TimingPart.Run)))
				.Take(count);
			foreach (int key in keys)
			{
				int run = HandlerTimings.Index(key, TimingPart.Run);
				int wait = HandlerTimings.Index(key, TimingPart.LockWait);
				long n = timingCurrent.Count(timingBaseline, run);
				double total = timingCurrent.TotalMs(timingBaseline, run);
				long runMax = HandlerTimings.MaxSinceReset(run);
				long waitMax = HandlerTimings.MaxSinceReset(wait);
				yield return $"  {HandlerTimings.KeyName(key),-24} n {n,7}  total {total,9:0.0}  avg {total / n,7:0.000}  p99 {timingCurrent.P99Ms(timingBaseline, run, runMax),7:0.0}  " +
				             $"max {HandlerTimings.TicksToMs(runMax),7:0.0}  wait p99 {timingCurrent.P99Ms(timingBaseline, wait, waitMax),6:0.0}  wait max {HandlerTimings.TicksToMs(waitMax),7:0.0}";
			}
		}

		private static IEnumerable<string> TopTraffic(int count)
		{
			var types = Enumerable.Range(0, TrafficCounters.TypeCount)
				.Select(t => new
				{
					Type = t,
					SentBytes = current.Bytes(baseline, t, PerfDirection.Sent),
					RecvBytes = current.Bytes(baseline, t, PerfDirection.Received),
				})
				.Where(t => t.SentBytes + t.RecvBytes > 0)
				.OrderByDescending(t => t.SentBytes + t.RecvBytes)
				.Take(count);
			foreach (var t in types)
			{
				yield return $"    {TrafficCounters.TypeName(t.Type),-28} sent {current.Messages(baseline, t.Type, PerfDirection.Sent),7} msgs {Kb(t.SentBytes),11}   " +
				             $"recv {current.Messages(baseline, t.Type, PerfDirection.Received),7} msgs {Kb(t.RecvBytes),11}";
			}
		}

		private static (int Key, double Ms) Worst(TimingPart part)
		{
			int worstKey = -1;
			long worstTicks = -1;
			for (int key = 0; key < HandlerTimings.KeyCount; key++)
			{
				int index = HandlerTimings.Index(key, part);
				if (timingCurrent.Count(timingBaseline, index) == 0) continue;
				long ticks = HandlerTimings.MaxSinceReset(index);
				if (ticks <= worstTicks) continue;
				worstTicks = ticks;
				worstKey = key;
			}
			return (worstKey, worstKey < 0 ? 0 : HandlerTimings.TicksToMs(worstTicks));
		}

		private static string Since => resetByCommand ? "reset" : "start";

		private static string Rate(double bytesPerSecond) => $"{bytesPerSecond / 1024.0:0.0} kB/s";

		private static string Kb(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.00} MB" : $"{bytes / 1024.0:0.0} kB";

		private static string Mb(long bytes) => $"{bytes / 1048576.0:0.0} MB";

		private static string FormatDuration(double seconds)
		{
			var span = TimeSpan.FromSeconds(Math.Round(seconds));
			return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
		}
	}
}
