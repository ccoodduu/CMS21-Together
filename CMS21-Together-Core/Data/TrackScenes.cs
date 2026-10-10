using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Core.Data;

public static class TrackScenes
{
	public sealed class Track
	{
		public Track(GameScene scene, CarAwayKind kind, string loadName, string name, params string[] unityNames)
		{
			Scene = scene;
			Kind = kind;
			LoadName = loadName;
			Name = name;
			UnityNames = unityNames;
		}

		public GameScene Scene { get; }
		public CarAwayKind Kind { get; }
		public string LoadName { get; }
		public string Name { get; }
		public string[] UnityNames { get; }
	}

	public static readonly IReadOnlyList<Track> All = new[]
	{
		new Track(GameScene.TestTrack, CarAwayKind.TestTrack, "Test_track_1", "test track", "Test_track_1"),
		new Track(GameScene.RaceTrack, CarAwayKind.RaceTrack, "Race_track_1", "race track", "Race_track_1"),
		new Track(GameScene.SpeedTrack, CarAwayKind.SpeedTrack, "Speedtrack", "speed track", "SpeedTrack", "Speedtrack"),
	};

	public static Track Get(GameScene scene) => All.FirstOrDefault(t => t.Scene == scene);

	public static Track Get(CarAwayKind kind) => All.FirstOrDefault(t => t.Kind == kind);

	public static bool IsTrack(GameScene scene) => Get(scene) != null;

	public static bool IsTrackKind(CarAwayKind kind) => Get(kind) != null;

	public static CarAwayKind KindOf(GameScene scene) => Get(scene)?.Kind ?? CarAwayKind.TestTrack;

	public static GameScene SceneOf(CarAwayKind kind) => Get(kind)?.Scene ?? GameScene.Unknown;

	public static GameScene ByUnityName(string sceneName) =>
		All.FirstOrDefault(t => t.UnityNames.Any(n => string.Equals(n, sceneName, StringComparison.OrdinalIgnoreCase)))?.Scene ?? GameScene.Unknown;

	public static string NameOf(GameScene scene) => Get(scene)?.Name ?? scene.ToString();
}
