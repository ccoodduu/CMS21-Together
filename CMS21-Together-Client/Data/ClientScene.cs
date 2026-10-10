using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;

namespace CMS21Together.Data;

public static class ClientScene
{
	private static readonly Queue<Action> pendingGarageActions = new Queue<Action>();

	public static GameScene LocalScene { get; set; } = GameScene.Menu;

	public static GameScene Destination { get; set; } = GameScene.Unknown;

	public static bool IsGarageReady => LocalScene == GameScene.Garage;

	public static event Action<GameScene, GameScene> LeavingScene;

	public static void RaiseLeavingScene(GameScene from, GameScene to) => LeavingScene?.Invoke(from, to);

	public static void GarageBound(Action apply, Action mirrorOnly = null)
	{
		if (!IsGarageReady)
		{
			mirrorOnly?.Invoke();
			return;
		}
		if (SyncTracker.InSnapshot || ClientData.IsInitialSyncFinished)
		{
			apply();
			return;
		}
		pendingGarageActions.Enqueue(apply);
	}

	public static void DrainPending()
	{
		while (pendingGarageActions.Count > 0)
		{
			var action = pendingGarageActions.Dequeue();
			try
			{
				action();
			}
			catch (Exception ex)
			{
				Log.Error($"[ClientScene] Queued garage packet failed: {ex.Message}");
			}
		}
	}

	public static void ClearPending() => pendingGarageActions.Clear();

	public static SceneType ToSceneType(GameScene scene) =>
		Enum.TryParse(scene.ToString(), out SceneType sceneType) ? sceneType : SceneType.None;

	public static GameScene FromSceneType(SceneType sceneType)
	{
		if (sceneType == SceneType.None) return GameScene.Unknown;
		return Enum.TryParse(sceneType.ToString(), out GameScene scene) ? scene : GameScene.Unknown;
	}

	public static GameScene FromSceneName(string sceneName)
	{
		switch ((sceneName ?? "").ToLowerInvariant())
		{
			case "garage": return GameScene.Garage;
			case "menu": return GameScene.Menu;
			default: return TrackScenes.ByUnityName(sceneName);
		}
	}

	public static bool IsTransitionScene(string sceneName)
	{
		switch ((sceneName ?? "").ToLowerInvariant())
		{
			case "sceneloader":
			case "loadresources":
			case "introplayway":
				return true;
			default:
				return false;
		}
	}
}
