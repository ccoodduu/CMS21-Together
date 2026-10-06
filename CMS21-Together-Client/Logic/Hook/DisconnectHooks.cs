using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using HarmonyLib;

namespace CMS21Together.Logic.Hook
{
	[HarmonyPatch]
	public static class DisconnectHooks
	{
		[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.SelectSceneToLoad),
			typeof(string), typeof(SceneType), typeof(bool), typeof(bool))]
		[HarmonyPrefix]
		public static void SelectSceneToLoadPrefix(string newSceneName, SceneType sceneType, bool useFader, bool saveGame)
		{
			if (Client.Instance == null || !Client.Instance.IsConnected) return;

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

			if (ClientData.IsInitialSyncFinished)
			{
				ClientScene.LocalScene = GameScene.Loading;
				PresenceManager.PublishLocal();
				PresenceManager.ReconcileAll();
			}
		}
	}
}
