using System;
using CMS21_Together_Core.Data.Enum;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.CarDriveStart)]
public class CarDriveStartPacket : INetworkData
{
	public int PlayerId;
	public int DriveId;
	public GameScene Scene;
	public int CarLoaderID = -1;
	public string CarToLoad;
	public byte[] CarBlob;
	public byte CarBlobVersion;
}

[Serializable]
[NetworkPacket(PacketTypes.CarDriveState)]
public class CarDriveStatePacket : INetworkData
{
	public int PlayerId;
	public int DriveId;
	public int Seq;
	public byte[] Payload;
}

[Serializable]
[NetworkPacket(PacketTypes.CarDriveStop)]
public class CarDriveStopPacket : INetworkData
{
	public int PlayerId;
	public int DriveId;
	public byte[] FinalPose;
}

[Flags]
public enum DriveFlags : byte
{
	None = 0,
	Brake = 1,
	Lights = 2,
	EngineRunning = 4,
	Reverse = 8
}

public struct DriveState
{
	public float Time;
	public float PosX, PosY, PosZ;
	public float RotX, RotY, RotZ, RotW;
	public float VelX, VelY, VelZ;
	public float SteerDegrees;
	public float WheelRadPerSecond;
	public float Rpm;
	public int Gear;
	public DriveFlags Flags;
}

public static class DriveStateCodec
{
	public const int Size = 36;
	public const float SteerStep = 0.5f;
	public const float WheelStep = 0.01f;
	public const float VelocityStep = 0.01f;
	private const float RotationRange = 0.70710678f;
	private const float RotationScale = short.MaxValue / RotationRange;

	public static byte[] Encode(DriveState s)
	{
		var bytes = new byte[Size];
		int i = 0;
		PutFloat(bytes, ref i, s.Time);
		PutFloat(bytes, ref i, s.PosX);
		PutFloat(bytes, ref i, s.PosY);
		PutFloat(bytes, ref i, s.PosZ);
		PutRotation(bytes, ref i, s.RotX, s.RotY, s.RotZ, s.RotW);
		PutShort(bytes, ref i, Quantize(s.VelX, VelocityStep));
		PutShort(bytes, ref i, Quantize(s.VelY, VelocityStep));
		PutShort(bytes, ref i, Quantize(s.VelZ, VelocityStep));
		bytes[i++] = (byte)(sbyte)Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, Math.Round(s.SteerDegrees / SteerStep)));
		PutShort(bytes, ref i, Quantize(s.WheelRadPerSecond, WheelStep));
		ushort rpm = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, Math.Round(s.Rpm)));
		bytes[i++] = (byte)rpm;
		bytes[i++] = (byte)(rpm >> 8);
		bytes[i++] = (byte)(sbyte)Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, s.Gear));
		bytes[i] = (byte)s.Flags;
		return bytes;
	}

	public static bool TryDecode(byte[] bytes, out DriveState s)
	{
		s = default;
		if (bytes == null || bytes.Length != Size) return false;
		int i = 0;
		s.Time = GetFloat(bytes, ref i);
		s.PosX = GetFloat(bytes, ref i);
		s.PosY = GetFloat(bytes, ref i);
		s.PosZ = GetFloat(bytes, ref i);
		GetRotation(bytes, ref i, out s.RotX, out s.RotY, out s.RotZ, out s.RotW);
		s.VelX = GetShort(bytes, ref i) * VelocityStep;
		s.VelY = GetShort(bytes, ref i) * VelocityStep;
		s.VelZ = GetShort(bytes, ref i) * VelocityStep;
		s.SteerDegrees = (sbyte)bytes[i++] * SteerStep;
		s.WheelRadPerSecond = GetShort(bytes, ref i) * WheelStep;
		s.Rpm = bytes[i] | (bytes[i + 1] << 8);
		i += 2;
		s.Gear = (sbyte)bytes[i++];
		s.Flags = (DriveFlags)bytes[i];
		return IsFinite(s.Time) && IsFinite(s.PosX) && IsFinite(s.PosY) && IsFinite(s.PosZ);
	}

	private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

	private static short Quantize(float value, float step)
	{
		if (!IsFinite(value)) return 0;
		return (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(value / step)));
	}

	private static void PutFloat(byte[] b, ref int i, float value)
	{
		var raw = BitConverter.GetBytes(value);
		if (!BitConverter.IsLittleEndian) Array.Reverse(raw);
		Buffer.BlockCopy(raw, 0, b, i, 4);
		i += 4;
	}

	private static float GetFloat(byte[] b, ref int i)
	{
		var raw = new byte[4];
		Buffer.BlockCopy(b, i, raw, 0, 4);
		if (!BitConverter.IsLittleEndian) Array.Reverse(raw);
		i += 4;
		return BitConverter.ToSingle(raw, 0);
	}

	private static void PutShort(byte[] b, ref int i, short value)
	{
		b[i++] = (byte)value;
		b[i++] = (byte)(value >> 8);
	}

	private static short GetShort(byte[] b, ref int i)
	{
		short value = (short)(b[i] | (b[i + 1] << 8));
		i += 2;
		return value;
	}

	private static void PutRotation(byte[] b, ref int i, float x, float y, float z, float w)
	{
		float length = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
		if (length < 1e-6f || !IsFinite(length)) { x = 0; y = 0; z = 0; w = 1; length = 1; }
		var q = new[] { x / length, y / length, z / length, w / length };
		int largest = 0;
		for (int k = 1; k < 4; k++) if (Math.Abs(q[k]) > Math.Abs(q[largest])) largest = k;
		float sign = q[largest] < 0 ? -1f : 1f;
		for (int k = 0; k < 4; k++)
		{
			if (k == largest) continue;
			PutShort(b, ref i, (short)Math.Max(-short.MaxValue, Math.Min(short.MaxValue, Math.Round(q[k] * sign * RotationScale))));
		}
		b[i++] = (byte)largest;
	}

	private static void GetRotation(byte[] b, ref int i, out float x, out float y, out float z, out float w)
	{
		var q = new float[4];
		var small = new float[3];
		for (int k = 0; k < 3; k++) small[k] = GetShort(b, ref i) / RotationScale;
		int largest = b[i++] & 3;
		float sum = 0;
		for (int k = 0, s = 0; k < 4; k++)
		{
			if (k == largest) continue;
			q[k] = small[s++];
			sum += q[k] * q[k];
		}
		q[largest] = (float)Math.Sqrt(Math.Max(0f, 1f - sum));
		x = q[0];
		y = q[1];
		z = q[2];
		w = q[3];
	}
}
