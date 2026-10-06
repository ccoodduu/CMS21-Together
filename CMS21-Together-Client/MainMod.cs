using System;
using System.IO;
using BuildInfo = CMS21_Together_Core.BuildInfo;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network;
using CMS21Together.Data;
using CMS21Together.Guard;
using CMS21Together.Logic.Player;
using CMS21Together.Logging;
using CMS21Together.Managers;
using CMS21Together.Network;
using CMS21Together.Persistence;
using CMS21Together.Session;
using CMS21Together.UI;
using MelonLoader;
using Steamworks;
using UnityEngine;

// ReSharper disable All

namespace CMS21Together
{
	public class MainMod : MelonMod
	{
		public const int MAX_PLAYER = 4;
		public const int PORT = NetworkConstants.DEFAULT_PORT;
		public const string ASSEMBLY_MOD_VERSION = BuildInfo.ModVersion;
		public const string MOD_VERSION = "Together " + ASSEMBLY_MOD_VERSION;
		
		public bool isModInitialized;
		public static bool IsSteamAvailable { get; private set; }

		public override void OnLateInitializeMelon()
		{
			Log.SetLogger(new ClientLoggerAdapter());

			InitializeSteam();

			PacketRouter.Initialize(System.Reflection.Assembly.GetExecutingAssembly());
			Client.Init();
			JoinRequests.Initialize();

			Log.Info($"Together Mod {BuildInfo.FullVersion} initialized!");
			isModInitialized = true;
		}
		
		private void InitializeSteam()
		{
			string dllPath = Path.Combine(Directory.GetCurrentDirectory(), "UserLibs", "steam_api64.dll");

			if (!File.Exists(dllPath))
			{
				IsSteamAvailable = false;
				Log.Warn("Steam DLL not found in UserLibs. Switching to Non-Steam mode.");
				return;
			}
			
			try 
			{
				SteamClient.Init(1190000);
				if (!SteamClient.IsValid || SteamClient.AppId.Value != 1190000)
				{
					IsSteamAvailable = false;
					SteamClient.Shutdown();
					Log.Warn("Steam environment invalid or emulated. Features disabled.");
					return;
				}

				SteamNetworkingUtils.InitRelayNetworkAccess();
				IsSteamAvailable = true;
				Log.Success("Steamworks initialized successfully.");
			}
			catch (Exception)
			{
				IsSteamAvailable = false;
				SteamClient.Shutdown();
				Log.Warn("Steamworks could not be initialized (Non-Steam version or Steam not running). Steam features will be disabled.");
			}
		}

		public override void OnSceneWasLoaded(int buildindex, string sceneName)
		{
			if (sceneName == "Menu") SessionGuard.End();
		}

		public override void OnSceneWasInitialized(int buildindex, string sceneName)
		{
			SceneReady.OnSceneInitialized(sceneName);
			if (sceneName == "Menu") JoinRequests.OnMenuInitialized();
		}

		public override void OnUpdate()
		{
			if (!isModInitialized )
				return;
			
			if (PlayerSettings.DevHotkeys && Input.GetKeyDown(KeyCode.F5))
			{
				string target = string.IsNullOrWhiteSpace(PlayerSettings.LastJoinTarget) ? "127.0.0.1" : PlayerSettings.LastJoinTarget;
				if (!JoinService.Join(target, out string error)) Log.Warn($"[Join] {error}");
			}
			if (Client.Instance.IsConnectionValid && Input.GetKeyDown(PlayerSettings.ResyncKey)) ResyncController.Request();
			ConnectionStatus.Update();

			if (Client.Instance.IsConnectionValid)
				ClientData.Update();

			if (IsSteamAvailable)
			{
				SteamClient.RunCallbacks();
				if (Client.Instance.IsConnected) Client.Instance.Steam?.Receive();
			}
			ThreadManager.UpdateThread();
		}

		public override void OnLateUpdate() { }

		public override void OnGUI()
		{
			if (!isModInitialized) return;
			if (Client.Instance.IsConnectionValid) NameTags.Draw();
			ImguiView.Draw();
		}

		public override void OnInitializeMelon()
		{
			ModConsole.Initialize();
			PlayerSettings.Initialize();
			GuardSettings.Initialize();
		}

		public override void OnApplicationQuit()
		{
			SessionGuard.End();
			RichPresence.Clear();
		}
	}
}