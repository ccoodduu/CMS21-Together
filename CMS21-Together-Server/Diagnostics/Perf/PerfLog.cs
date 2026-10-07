using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CMS21_Together_Server.Network;
using CMS21_Together_Server.Log;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Diagnostics.Perf
{
	public struct ProcessReading
	{
		public double At;
		public TimeSpan Cpu;
		public long PrivateBytes;
		public long WorkingSet;
		public long ManagedHeap;
		public int Threads;
		public int Handles;
		public int Gc0, Gc1, Gc2;

		public static ProcessReading Read()
		{
			using (var process = Process.GetCurrentProcess())
			{
				return new ProcessReading
				{
					At = PerfLog.Now,
					Cpu = process.TotalProcessorTime,
					PrivateBytes = process.PrivateMemorySize64,
					WorkingSet = process.WorkingSet64,
					ManagedHeap = GC.GetTotalMemory(false),
					Threads = process.Threads.Count,
					Handles = process.HandleCount,
					Gc0 = GC.CollectionCount(0),
					Gc1 = GC.CollectionCount(1),
					Gc2 = GC.CollectionCount(2),
				};
			}
		}

		public double CpuPercentSince(ProcessReading earlier)
		{
			double wall = At - earlier.At;
			return wall > 0 ? (Cpu - earlier.Cpu).TotalSeconds / wall * 100.0 : 0;
		}
	}

	public struct ClientCounts
	{
		public int Connected, Syncing, InSession;

		public static ClientCounts Read()
		{
			var counts = new ClientCounts();
			foreach (var client in Network.Server.Clients.Values)
			{
				if (!client.IsConnected) continue;
				counts.Connected++;
				if (client.SyncState == SyncState.Syncing) counts.Syncing++;
				else if (client.SyncState == SyncState.InSession) counts.InSession++;
			}
			return counts;
		}
	}

	public static class PerfLog
	{
		private static readonly Stopwatch clock = Stopwatch.StartNew();
		private static readonly object saveStatsGate = new object();
		private static readonly string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");

		private static readonly TrafficSnapshot trafficPrevious = new TrafficSnapshot();
		private static readonly TrafficSnapshot trafficCurrent = new TrafficSnapshot();
		private static readonly TimingSnapshot timingPrevious = new TimingSnapshot();
		private static readonly TimingSnapshot timingCurrent = new TimingSnapshot();
		private static ProcessReading processPrevious;
		private static double nextLineAt;

		private static long lastSaveBytes = -1;
		private static double lastSaveMs;
		private static DateTime lastSaveAt;

		public static double Now => clock.Elapsed.TotalSeconds;
		public static int IntervalSeconds { get; private set; }
		public static string FilePath { get; private set; }

		public static void Initialize(int intervalSeconds)
		{
			IntervalSeconds = Math.Max(0, intervalSeconds);
			PerfSummary.Reset();
			if (IntervalSeconds == 0) return;

			Directory.CreateDirectory(logDirectory);
			FilePath = Path.Combine(logDirectory, $"perf_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jsonl");
			TrafficCounters.Capture(trafficPrevious);
			HandlerTimings.Capture(timingPrevious);
			for (int i = 0; i < HandlerTimings.KeyCount * 2; i++) HandlerTimings.TakeMax(i);
			processPrevious = ProcessReading.Read();
			nextLineAt = Now + IntervalSeconds;
			Logger.Info($"[Perf] Writing a line every {IntervalSeconds} s to {FilePath}");
		}

		public static void RecordSave(long bytes, double ms)
		{
			lock (saveStatsGate)
			{
				lastSaveBytes = bytes;
				lastSaveMs = ms;
				lastSaveAt = DateTime.Now;
			}
		}

		public static (long Bytes, double Ms, DateTime At) LastSave()
		{
			lock (saveStatsGate) return (lastSaveBytes, lastSaveMs, lastSaveAt);
		}

		public static void Tick()
		{
			PerfSummary.Tick();
			if (IntervalSeconds == 0 || Now < nextLineAt) return;
			nextLineAt += IntervalSeconds;
			if (nextLineAt < Now) nextLineAt = Now + IntervalSeconds;

			try
			{
				File.AppendAllText(FilePath, BuildLine() + Environment.NewLine);
			}
			catch (Exception ex)
			{
				Logger.Warn($"[Perf] Could not write {FilePath}: {ex.Message}");
			}
		}

		private static string BuildLine()
		{
			var process = ProcessReading.Read();
			TrafficCounters.Capture(trafficCurrent);
			HandlerTimings.Capture(timingCurrent);
			var clients = ClientCounts.Read();
			var save = LastSave();
			double interval = process.At - processPrevious.At;

			var line = new JObject
			{
				["t"] = Timestamp(DateTime.Now),
				["uptimeS"] = Round(process.At),
				["intervalS"] = Round(interval),
				["cpuPct"] = Round(process.CpuPercentSince(processPrevious)),
				["privateBytes"] = process.PrivateBytes,
				["workingSet"] = process.WorkingSet,
				["managedHeap"] = process.ManagedHeap,
				["threads"] = process.Threads,
				["handles"] = process.Handles,
				["gc"] = new JArray(process.Gc0, process.Gc1, process.Gc2),
				["clients"] = new JObject { ["connected"] = clients.Connected, ["syncing"] = clients.Syncing, ["inSession"] = clients.InSession },
				["save"] = save.Bytes < 0 ? null : new JObject
				{
					["bytes"] = save.Bytes,
					["ms"] = Round(save.Ms),
					["at"] = Timestamp(save.At),
				},
				["logBytes"] = DirectorySize(logDirectory),
				["desyncRecords"] = FileCount(Path.Combine(logDirectory, "desync")),
				["slots"] = SlotRows(trafficCurrent, trafficPrevious),
				["traffic"] = TrafficRows(trafficCurrent, trafficPrevious),
				["slotTraffic"] = SlotTrafficRows(trafficCurrent, trafficPrevious),
				["timings"] = TimingRows(timingCurrent, timingPrevious),
			};

			trafficPrevious.CopyFrom(trafficCurrent);
			timingPrevious.CopyFrom(timingCurrent);
			processPrevious = process;
			return line.ToString(Formatting.None);
		}

		private static JArray SlotRows(TrafficSnapshot current, TrafficSnapshot previous)
		{
			var rows = new JArray();
			for (int slot = 0; slot < TrafficCounters.SlotCount; slot++)
			{
				long downMessages = current.SlotTotal(previous, slot, PerfDirection.Sent, false);
				long upMessages = current.SlotTotal(previous, slot, PerfDirection.Received, false);
				bool connected = Network.Server.Clients.TryGetValue(slot, out var client) && client.IsConnected;
				if (downMessages == 0 && upMessages == 0 && !connected) continue;
				rows.Add(new JObject
				{
					["slot"] = slot,
					["state"] = connected ? client.SyncState.ToString() : "Disconnected",
					["down"] = current.SlotTotal(previous, slot, PerfDirection.Sent, true),
					["up"] = current.SlotTotal(previous, slot, PerfDirection.Received, true),
					["downMsgs"] = downMessages,
					["upMsgs"] = upMessages,
				});
			}
			return rows;
		}

		private static JArray TrafficRows(TrafficSnapshot current, TrafficSnapshot previous)
		{
			var rows = new JArray();
			for (int type = 0; type < TrafficCounters.TypeCount; type++)
			for (int direction = 0; direction < TrafficCounters.DirectionCount; direction++)
			for (int transport = 0; transport < TrafficCounters.TransportCount; transport++)
			{
				int index = TrafficCounters.TypeIndex(type, (PerfDirection)direction, (PerfTransport)transport);
				long messages = TrafficSnapshot.Delta(current.TypeMessages, previous.TypeMessages, index);
				if (messages == 0) continue;
				rows.Add(new JObject
				{
					["type"] = TrafficCounters.TypeName(type),
					["dir"] = DirectionName((PerfDirection)direction),
					["transport"] = ((PerfTransport)transport).ToString().ToLowerInvariant(),
					["msgs"] = messages,
					["bytes"] = TrafficSnapshot.Delta(current.TypeBytes, previous.TypeBytes, index),
				});
			}
			return rows;
		}

		private static JArray SlotTrafficRows(TrafficSnapshot current, TrafficSnapshot previous)
		{
			var rows = new JArray();
			for (int slot = 0; slot < TrafficCounters.SlotCount; slot++)
			for (int type = 0; type < TrafficCounters.TypeCount; type++)
			for (int direction = 0; direction < TrafficCounters.DirectionCount; direction++)
			{
				int index = TrafficCounters.SlotIndex(slot, type, (PerfDirection)direction);
				long messages = TrafficSnapshot.Delta(current.SlotMessages, previous.SlotMessages, index);
				if (messages == 0) continue;
				rows.Add(new JObject
				{
					["slot"] = slot,
					["type"] = TrafficCounters.TypeName(type),
					["dir"] = DirectionName((PerfDirection)direction),
					["msgs"] = messages,
					["bytes"] = TrafficSnapshot.Delta(current.SlotBytes, previous.SlotBytes, index),
				});
			}
			return rows;
		}

		private static JArray TimingRows(TimingSnapshot current, TimingSnapshot previous)
		{
			var rows = new JArray();
			for (int key = 0; key < HandlerTimings.KeyCount; key++)
			{
				int wait = HandlerTimings.Index(key, TimingPart.LockWait);
				int run = HandlerTimings.Index(key, TimingPart.Run);
				long count = current.Count(previous, run);
				long waitMax = HandlerTimings.TakeMax(wait);
				long runMax = HandlerTimings.TakeMax(run);
				if (count == 0) continue;
				rows.Add(new JObject
				{
					["key"] = HandlerTimings.KeyName(key),
					["n"] = count,
					["waitMs"] = Round(current.TotalMs(previous, wait)),
					["waitMaxMs"] = Round(HandlerTimings.TicksToMs(waitMax)),
					["waitP99Ms"] = Round(current.P99Ms(previous, wait, waitMax)),
					["runMs"] = Round(current.TotalMs(previous, run)),
					["runMaxMs"] = Round(HandlerTimings.TicksToMs(runMax)),
					["runP99Ms"] = Round(current.P99Ms(previous, run, runMax)),
				});
			}
			return rows;
		}

		private static string Timestamp(DateTime time) => time.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

		public static string DirectionName(PerfDirection direction) => direction == PerfDirection.Sent ? "sent" : "recv";

		public static double Round(double value) => double.IsInfinity(value) || double.IsNaN(value) ? -1 : Math.Round(value, 3);

		private static long DirectorySize(string path)
		{
			try
			{
				return Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
			}
			catch (IOException) { return -1; }
			catch (UnauthorizedAccessException) { return -1; }
		}

		private static int FileCount(string path)
		{
			try
			{
				return Directory.Exists(path) ? Directory.EnumerateFiles(path).Count() : 0;
			}
			catch (IOException) { return -1; }
			catch (UnauthorizedAccessException) { return -1; }
		}

		public static long LogDirectorySize() => DirectorySize(logDirectory);

		public static int DesyncRecordCount() => FileCount(Path.Combine(logDirectory, "desync"));
	}
}
