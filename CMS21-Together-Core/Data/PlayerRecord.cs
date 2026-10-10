using System;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CMS21_Together_Core.Data;

public class PlayerRecord
{
	public string Key;
	public string Name;
	public DateTime LastSeenUtc;
	public Vector3Serializable Position;
	public QuaternionSerializable Rotation;
	[JsonConverter(typeof(StringEnumConverter))] public GameScene Scene = GameScene.Unknown;
	public long BestLapMs;
	public int TopSpeedKmh;
}
