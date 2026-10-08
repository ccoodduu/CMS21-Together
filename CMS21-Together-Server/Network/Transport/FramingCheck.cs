using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;

namespace CMS21_Together_Server.Network.Transport
{
	// --check-framing: a stream of length-framed packets comes out whole and in order however TCP splits it, including
	// a split inside a length prefix.
	public static class FramingCheck
	{
		public static int Run()
		{
			var random = new Random(20261008);
			var sent = new List<byte[]>();
			var stream = new List<byte>();
			for (int i = 0; i < 300; i++)
			{
				int size = i % 7 == 0 ? random.Next(4000, 20000) : random.Next(1, 300);
				var body = new byte[size];
				random.NextBytes(body);
				using (var packet = new Packet(i))
				{
					packet.Write(body);
					packet.WriteLength();
					var bytes = packet.ToArray();
					sent.Add(bytes.Skip(4).ToArray());
					stream.AddRange(bytes);
				}
			}
			var all = stream.ToArray();
			var insidePrefix = new Queue<int>();
			int start = 0;
			foreach (var body in sent)
			{
				insidePrefix.Enqueue(start + 2 - insidePrefix.Sum());
				start += 4 + body.Length;
			}

			int failures = 0;
			var patterns = new List<(string Name, Func<int> Next)>
			{
				("4096-byte reads", () => 4096),
				("1-byte reads", () => 1),
				("3-byte reads", () => 3),
				("4097-byte reads", () => 4097),
				("random reads", () => random.Next(1, 5000)),
				("reads ending inside every length prefix", () => insidePrefix.Count > 0 ? Math.Max(1, insidePrefix.Dequeue()) : 4096),
			};
			foreach (var pattern in patterns)
			{
				var framer = new PacketFramer();
				var received = new List<byte[]>();
				int offset = 0;
				var chunk = new byte[20000];
				while (offset < all.Length)
				{
					int length = Math.Min(pattern.Next(), all.Length - offset);
					Array.Copy(all, offset, chunk, 0, length);
					framer.Feed(chunk, length, received.Add);
					offset += length;
				}
				int intact = 0;
				while (intact < Math.Min(sent.Count, received.Count) && sent[intact].SequenceEqual(received[intact])) intact++;
				bool ok = received.Count == sent.Count && intact == sent.Count && framer.Buffered == 0;
				if (!ok) failures++;
				Console.WriteLine($"framing check, {pattern.Name}: {received.Count}/{sent.Count} packets, first {intact} intact, {framer.Buffered} bytes left -> {(ok ? "OK" : "FAILED")}");
			}
			return failures == 0 ? 0 : 1;
		}
	}
}
