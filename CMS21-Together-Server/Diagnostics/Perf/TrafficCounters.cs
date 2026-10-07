using System;
using System.Linq;
using System.Threading;
using CMS21_Together_Core;

namespace CMS21_Together_Server.Diagnostics.Perf
{
	public enum PerfTransport { Tcp, Udp, Steam }

	public enum PerfDirection { Sent, Received }

	public static class TrafficCounters
	{
		public const int TransportCount = 3;
		public const int DirectionCount = 2;

		public static readonly int TypeCount = Enum.GetValues(typeof(PacketTypes)).Cast<int>().Max() + 2;
		public static int UnknownType => TypeCount - 1;
		public static int SlotCount { get; private set; }

		private static readonly string[] typeNames = Enumerable.Range(0, TypeCount)
			.Select(i => i == TypeCount - 1 ? "Unknown" : Enum.IsDefined(typeof(PacketTypes), i) ? ((PacketTypes)i).ToString() : $"Type{i}")
			.ToArray();

		private static long[] typeMessages;
		private static long[] typeBytes;
		private static long[] slotMessages;
		private static long[] slotBytes;

		public static void Initialize(int maxPlayers)
		{
			SlotCount = maxPlayers + 1;
			typeMessages = new long[TypeCount * DirectionCount * TransportCount];
			typeBytes = new long[typeMessages.Length];
			slotMessages = new long[SlotCount * TypeCount * DirectionCount];
			slotBytes = new long[slotMessages.Length];
		}

		public static string TypeName(int type) => typeNames[type];

		public static int TypeIndex(int packetType) => packetType >= 0 && packetType < TypeCount - 1 ? packetType : TypeCount - 1;

		public static int TypeIndex(int type, PerfDirection direction, PerfTransport transport) =>
			(type * DirectionCount + (int)direction) * TransportCount + (int)transport;

		public static int SlotIndex(int slot, int type, PerfDirection direction) =>
			(slot * TypeCount + type) * DirectionCount + (int)direction;

		public static void CountSent(int slot, int packetType, PerfTransport transport, int bytes) =>
			Count(slot, packetType, PerfDirection.Sent, transport, bytes);

		public static void CountReceived(int slot, int packetType, PerfTransport transport, int bytes) =>
			Count(slot, packetType, PerfDirection.Received, transport, bytes);

		private static void Count(int slot, int packetType, PerfDirection direction, PerfTransport transport, int bytes)
		{
			var messages = typeMessages;
			if (messages == null) return;

			int type = TypeIndex(packetType);
			int typeIndex = TypeIndex(type, direction, transport);
			Interlocked.Increment(ref messages[typeIndex]);
			Interlocked.Add(ref typeBytes[typeIndex], bytes);

			int slotIndex = SlotIndex(slot >= 0 && slot < SlotCount ? slot : 0, type, direction);
			Interlocked.Increment(ref slotMessages[slotIndex]);
			Interlocked.Add(ref slotBytes[slotIndex], bytes);
		}

		public static long SlotBytes(int slot, PerfDirection direction)
		{
			if (slotBytes == null || slot < 0 || slot >= SlotCount) return 0;
			long total = 0;
			for (int type = 0; type < TypeCount; type++)
				total += Interlocked.Read(ref slotBytes[SlotIndex(slot, type, direction)]);
			return total;
		}

		public static void Capture(TrafficSnapshot into)
		{
			if (typeMessages == null) return;
			into.Ensure(typeMessages.Length, slotMessages.Length);
			for (int i = 0; i < typeMessages.Length; i++)
			{
				into.TypeMessages[i] = Interlocked.Read(ref typeMessages[i]);
				into.TypeBytes[i] = Interlocked.Read(ref typeBytes[i]);
			}
			for (int i = 0; i < slotMessages.Length; i++)
			{
				into.SlotMessages[i] = Interlocked.Read(ref slotMessages[i]);
				into.SlotBytes[i] = Interlocked.Read(ref slotBytes[i]);
			}
		}
	}

	public class TrafficSnapshot
	{
		public long[] TypeMessages = new long[0];
		public long[] TypeBytes = new long[0];
		public long[] SlotMessages = new long[0];
		public long[] SlotBytes = new long[0];

		internal void Ensure(int typeLength, int slotLength)
		{
			if (TypeMessages.Length != typeLength)
			{
				TypeMessages = new long[typeLength];
				TypeBytes = new long[typeLength];
			}
			if (SlotMessages.Length != slotLength)
			{
				SlotMessages = new long[slotLength];
				SlotBytes = new long[slotLength];
			}
		}

		public void CopyFrom(TrafficSnapshot other)
		{
			Ensure(other.TypeMessages.Length, other.SlotMessages.Length);
			Array.Copy(other.TypeMessages, TypeMessages, TypeMessages.Length);
			Array.Copy(other.TypeBytes, TypeBytes, TypeBytes.Length);
			Array.Copy(other.SlotMessages, SlotMessages, SlotMessages.Length);
			Array.Copy(other.SlotBytes, SlotBytes, SlotBytes.Length);
		}

		public long Messages(TrafficSnapshot since, int type, PerfDirection direction)
		{
			long total = 0;
			for (int t = 0; t < TrafficCounters.TransportCount; t++)
				total += Delta(TypeMessages, since?.TypeMessages, TrafficCounters.TypeIndex(type, direction, (PerfTransport)t));
			return total;
		}

		public long Bytes(TrafficSnapshot since, int type, PerfDirection direction)
		{
			long total = 0;
			for (int t = 0; t < TrafficCounters.TransportCount; t++)
				total += Delta(TypeBytes, since?.TypeBytes, TrafficCounters.TypeIndex(type, direction, (PerfTransport)t));
			return total;
		}

		public long SlotTotal(TrafficSnapshot since, int slot, PerfDirection direction, bool bytes)
		{
			long total = 0;
			var current = bytes ? SlotBytes : SlotMessages;
			var previous = bytes ? since?.SlotBytes : since?.SlotMessages;
			for (int type = 0; type < TrafficCounters.TypeCount; type++)
				total += Delta(current, previous, TrafficCounters.SlotIndex(slot, type, direction));
			return total;
		}

		public static long Delta(long[] current, long[] previous, int index) =>
			index < current.Length ? current[index] - (previous != null && index < previous.Length ? previous[index] : 0) : 0;
	}
}
