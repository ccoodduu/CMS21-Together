using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using HarmonyLib;

namespace CMS21Together.Logic.Hook
{
	[HarmonyPatch]
	public static class SceneHooks
	{
		[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.SelectSceneToLoad),
			typeof(string), typeof(SceneType), typeof(bool), typeof(bool))]
		[HarmonyPrefix]
		public static void SelectSceneToLoadPrefix(string newSceneName, SceneType sceneType, bool useFader, bool saveGame, bool __runOriginal)
		{
			if (!__runOriginal || Client.Instance == null || !Client.Instance.IsConnected) return;

			if (newSceneName == "Menu")
			{
				Client.Instance.Send(new DisconnectPacket()
				{
					playerID = Client.Instance.ID,
					message = "Player returned to menu."
				});
				Client.Instance.Disconnect();
				return;
			}

			if (!ClientData.IsInitialSyncFinished) return;

			var from = ClientScene.LocalScene;
			var to = ClientScene.FromSceneType(sceneType);
			Log.Info($"[Scene] Leaving {from} for {to} ({newSceneName}), profile slot {Singleton<GameManager>.Instance.ProfileManager.selectedProfile}.");
			ClientScene.RaiseLeavingScene(from, to);

			ClientScene.LocalScene = GameScene.Loading;
			PresenceManager.PublishLocal();
			PresenceManager.ReconcileAll();
			if (from == GameScene.Garage) GameData.Clear();
		}
	}
}
