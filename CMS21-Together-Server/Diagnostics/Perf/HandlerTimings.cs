using System;
using System.Diagnostics;
using System.Threading;

namespace CMS21_Together_Server.Diagnostics.Perf
{
	public enum TimingPart { LockWait, Run }

	public static class HandlerTimings
	{
		public static readonly int Tick = TrafficCounters.TypeCount;
		public static readonly int SaveBuild = TrafficCounters.TypeCount + 1;
		public static readonly int SnapshotBuild = TrafficCounters.TypeCount + 2;
		public static readonly int KeyCount = TrafficCounters.TypeCount + 3;

		public const int FineBuckets = 100;
		public const int CoarseBuckets = 990;
		public const int BucketCount = FineBuckets + CoarseBuckets + 1;

		private static readonly double MicrosPerTick = 1_000_000.0 / Stopwatch.Frequency;

		private static readonly long[] counts = new long[KeyCount * 2];
		private static readonly long[] totals = new long[KeyCount * 2];
		private static readonly long[] maxSinceReset = new long[KeyCount * 2];
		private static readonly long[] maxSinceTake = new long[KeyCount * 2];
		private static readonly long[][] histograms = new long[KeyCount * 2][];

		public static int KeyFor(int packetType) => TrafficCounters.TypeIndex(packetType);

		public static string KeyName(int key) =>
			key == Tick ? "tick" : key == SaveBuild ? "save-build" : key == SnapshotBuild ? "snapshot-build" : TrafficCounters.TypeName(key);

		public static int Index(int key, TimingPart part) => key * 2 + (int)part;

		public static void Record(int key, long lockWaitTicks, long runTicks)
		{
			if (key < 0 || key >= KeyCount) return;
			Add(Index(key, TimingPart.LockWait), lockWaitTicks);
			Add(Index(key, TimingPart.Run), runTicks);
		}

		private static void Add(int index, long ticks)
		{
			if (ticks < 0) ticks = 0;
			Interlocked.Increment(ref counts[index]);
			Interlocked.Add(ref totals[index], ticks);
			RaiseMax(ref maxSinceReset[index], ticks);
			RaiseMax(ref maxSinceTake[index], ticks);

			var histogram = Volatile.Read(ref histograms[index]);
			if (histogram == null)
			{
				var created = new long[BucketCount];
				histogram = Interlocked.CompareExchange(ref histograms[index], created, null) ?? created;
			}
			Interlocked.Increment(ref histogram[Bucket(ticks)]);
		}

		private static void RaiseMax(ref long max, long value)
		{
			long seen = Volatile.Read(ref max);
			while (value > seen)
			{
				long previous = Interlocked.CompareExchange(ref max, value, seen);
				if (previous == seen) return;
				seen = previous;
			}
		}

		private static int Bucket(long ticks)
		{
			double micros = ticks * MicrosPerTick;
			if (micros < 10_000) return (int)(micros / 100);
			if (micros < 1_000_000) return FineBuckets + (int)(micros / 1000) - 10;
			return BucketCount - 1;
		}

		public static double BucketUpperMs(int bucket) =>
			bucket < FineBuckets ? (bucket + 1) * 0.1 : bucket < BucketCount - 1 ? bucket - FineBuckets + 11 : double.PositiveInfinity;

		public static double TicksToMs(long ticks) => ticks * MicrosPerTick / 1000.0;

		public static long MaxSinceReset(int index) => Volatile.Read(ref maxSinceReset[index]);

		public static long TakeMax(int index) => Interlocked.Exchange(ref maxSinceTake[index], 0);

		public static void ResetMax()
		{
			for (int i = 0; i < maxSinceReset.Length; i++) Interlocked.Exchange(ref maxSinceReset[i], 0);
		}

		public static void Capture(TimingSnapshot into)
		{
			into.Ensure();
			for (int i = 0; i < counts.Length; i++)
			{
				long count = Interlocked.Read(ref counts[i]);
				into.Totals[i] = Interlocked.Read(ref totals[i]);
				if (count == into.Counts[i]) continue;
				into.Counts[i] = count;
				var histogram = Volatile.Read(ref histograms[i]);
				if (histogram == null) continue;
				if (into.Histograms[i] == null) into.Histograms[i] = new long[BucketCount];
				for (int b = 0; b < BucketCount; b++) into.Histograms[i][b] = Interlocked.Read(ref histogram[b]);
			}
		}
	}

	public class TimingSnapshot
	{
		public long[] Counts;
		public long[] Totals;
		public long[][] Histograms;

		internal void Ensure()
		{
			if (Counts != null) return;
			Counts = new long[HandlerTimings.KeyCount * 2];
			Totals = new long[Counts.Length];
			Histograms = new long[Counts.Length][];
		}

		public void CopyFrom(TimingSnapshot other)
		{
			Ensure();
			if (other.Counts == null) return;
			Array.Copy(other.Counts, Counts, Counts.Length);
			Array.Copy(other.Totals, Totals, Totals.Length);
			for (int i = 0; i < Histograms.Length; i++)
			{
				if (other.Histograms[i] == null) continue;
				if (Histograms[i] == null) Histograms[i] = new long[HandlerTimings.BucketCount];
				Array.Copy(other.Histograms[i], Histograms[i], HandlerTimings.BucketCount);
			}
		}

		public long Count(TimingSnapshot since, int index) => Counts[index] - (since?.Counts?[index] ?? 0);

		public double TotalMs(TimingSnapshot since, int index) => HandlerTimings.TicksToMs(Totals[index] - (since?.Totals?[index] ?? 0));

		public double P99Ms(TimingSnapshot since, int index, long maxTicks)
		{
			long count = Count(since, index);
			var current = Histograms[index];
			if (count <= 0 || current == null) return 0;
			var previous = since?.Histograms?[index];
			long target = (long)Math.Ceiling(count * 0.99);
			long seen = 0;
			for (int b = 0; b < HandlerTimings.BucketCount; b++)
			{
				seen += current[b] - (previous?[b] ?? 0);
				if (seen >= target)
					return Math.Min(HandlerTimings.BucketUpperMs(b), HandlerTimings.TicksToMs(maxTicks));
			}
			return HandlerTimings.TicksToMs(maxTicks);
		}
	}
}
