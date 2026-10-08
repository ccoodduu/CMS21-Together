using System.Collections;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;
using CMS21Together.Network;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Player;

public static class SceneReady
{
	private const float MaxWaitSeconds = 120f;

	public static void OnSceneInitialized(string sceneName)
	{
		if (!Client.Instance.IsConnectionValid || !ClientData.IsInitialSyncFinished) return;
		if (ClientScene.IsTransitionScene(sceneName)) return;

		var byName = ClientScene.FromSceneName(sceneName);
		if (byName == GameScene.Garage || byName == GameScene.Menu) return;

		MelonCoroutines.Start(WaitAndPublish(sceneName));
	}

	private static IEnumerator WaitAndPublish(string sceneName)
	{
		float deadline = Time.realtimeSinceStartup + MaxWaitSeconds;
		while (!NotificationCenter.IsGameReady || SceneLoader.BlockProgress || GameScript.Get() == null)
		{
			if (Time.realtimeSinceStartup > deadline)
			{
				Log.Warn($"[Scene] {sceneName} did not become ready within {MaxWaitSeconds}s.");
				yield break;
			}
			yield return null;
		}
		if (!Client.Instance.IsConnectionValid || ClientScene.LocalScene != GameScene.Loading) yield break;
		yield return Outdoor.OutdoorArrival.Apply();
		while (!Outdoor.OutdoorSession.ReadyToPublish)
		{
			if (Time.realtimeSinceStartup > deadline)
			{
				Log.Warn($"[Scene] {sceneName}: the outdoor instance was not applied within {MaxWaitSeconds}s.");
				break;
			}
			yield return null;
		}
		if (!Client.Instance.IsConnectionValid || ClientScene.LocalScene != GameScene.Loading) yield break;

		var scene = ClientScene.FromSceneType(GameScript.Get().CurrentSceneType);
		ClientScene.LocalScene = scene;
		PresenceManager.FindLocalMotor();
		SpawnPlacement.PlaceLocalPlayer();
		PresenceManager.PublishLocal();
		Movement.ForceSend();
		PresenceManager.ReconcileAll();
		Log.Info($"[Scene] {sceneName} ready as {scene}.");
	}
}
