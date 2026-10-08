using System;

namespace CMS21_Together_Core.Network
{
	// Cuts a TCP byte stream into the packets framed by a 4-byte length. Bytes of an incomplete packet, including a
	// length prefix split across two reads, wait for the next read.
	public sealed class PacketFramer
	{
		private byte[] buffer = new byte[8192];
		private int count;

		public int Buffered => count;

		// False when a length of zero or less shows the stream is corrupt; the buffered bytes are then dropped.
		public bool Feed(byte[] data, int length, Action<byte[]> onPacket)
		{
			if (count + length > buffer.Length) Array.Resize(ref buffer, Math.Max(buffer.Length * 2, count + length));
			Buffer.BlockCopy(data, 0, buffer, count, length);
			count += length;

			int offset = 0;
			while (count - offset >= 4)
			{
				int packetLength = BitConverter.ToInt32(buffer, offset);
				if (packetLength <= 0)
				{
					count = 0;
					return false;
				}
				if (count - offset - 4 < packetLength) break;
				var packet = new byte[packetLength];
				Buffer.BlockCopy(buffer, offset + 4, packet, 0, packetLength);
				offset += 4 + packetLength;
				onPacket(packet);
			}
			if (offset > 0)
			{
				Buffer.BlockCopy(buffer, offset, buffer, 0, count - offset);
				count -= offset;
			}
			return true;
		}
	}
}
