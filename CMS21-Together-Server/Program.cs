using System;
using System.IO;
using System.Reflection;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Persistence;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Network.Handlers;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server
{
	internal class Program
	{
		public const string MOD_VERSION = BuildInfo.ModVersion;

		public const int CONNECTION_TIMEOUT = 10;

		public static ServerConfig Config { get; private set; }
		
		static void SetupLogging()
		{
			string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
			if (!Directory.Exists(logDirectory))
			{
				Directory.CreateDirectory(logDirectory);
			}
			
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
			string uniqueLogFileName = $"Log_{timestamp}.txt";
			string uniqueLogPath = Path.Combine(logDirectory, uniqueLogFileName);
			
			string latestLogPath = Path.Combine(logDirectory, "Latest.txt");
			
			MultiTextWriter multiWriter = new MultiTextWriter(uniqueLogPath, latestLogPath);
			Console.SetOut(multiWriter);
		}
		
		public static void Main(string[] args)
		{
			string checkSavePath = GetArgument(args, "--check-save");
			if (checkSavePath != null)
			{
				Environment.Exit(CheckSave(checkSavePath));
				return;
			}

			if (Array.IndexOf(args, "--check-jobs") >= 0)
			{
				Environment.Exit(Data.Jobs.JobsCheck.Run());
				return;
			}

			if (Array.IndexOf(args, "--check-locks") >= 0)
			{
				Environment.Exit(Data.Cars.CarLocksCheck.Run());
				return;
			}

			if (Array.IndexOf(args, "--check-merges") >= 0)
			{
				Environment.Exit(Data.Cars.MergeCheck.Run());
				return;
			}

			if (Array.IndexOf(args, "--check-framing") >= 0)
			{
				Environment.Exit(Network.Transport.FramingCheck.Run());
				return;
			}

			if (Array.IndexOf(args, "--check-garage-look") >= 0)
			{
				Environment.Exit(Data.Garage.GarageLookCheck.Run());
				return;
			}

			if (Array.IndexOf(args, "--check-digest") >= 0)
			{
				Environment.Exit(Data.Reconciliation.DigestCheck.Run());
				return;
			}

			if (GetArgument(args, "--self-test") == "outdoor")
			{
				Environment.Exit(Data.Outdoor.OutdoorSelfTest.Run());
				return;
			}

			if (Array.IndexOf(args, "--check-redaction") >= 0)
			{
				Environment.Exit(Data.Diagnostics.RedactionCheck.Run());
				return;
			}

			string checkModsPath = GetArgument(args, "--check-mods");
			if (checkModsPath != null)
			{
				Environment.Exit(ModCheck.Run(checkModsPath));
				return;
			}

			Terminal.Gui.Application.Init();
			
			Terminal.Gui.Colors.Base.Normal = Terminal.Gui.Application.Driver.MakeAttribute(Terminal.Gui.Color.White, Terminal.Gui.Color.Black);
			Terminal.Gui.Colors.Base.Focus = Terminal.Gui.Application.Driver.MakeAttribute(Terminal.Gui.Color.White, Terminal.Gui.Color.Black);
			Terminal.Gui.Colors.Base.HotNormal = Terminal.Gui.Application.Driver.MakeAttribute(Terminal.Gui.Color.Cyan, Terminal.Gui.Color.Black);
			Terminal.Gui.Colors.Base.HotFocus = Terminal.Gui.Application.Driver.MakeAttribute(Terminal.Gui.Color.Cyan, Terminal.Gui.Color.Black);
			
			var window = new ServerWindow();
			
			SetupLogging();
			CMS21_Together_Core.Logging.Log.SetLogger(new ServerLoggerAdapter());
			Logger.Info($"CMS21 Together Server v{BuildInfo.FullVersion}");
			PacketRouter.Initialize(Assembly.GetExecutingAssembly());
			
			Config = ServerConfig.LoadOrCreate();
			Config.ApplyArguments(args);
			Logger.Info($"Settings: {Config.Describe()}");
			Data.Reconciliation.ReconciliationService.IntervalSeconds = Config.DesyncCheckIntervalSeconds;
			Data.Reconciliation.ReconciliationService.AutoFix = Config.DesyncAutofix;
			Data.Reconciliation.ReconciliationService.ResendKeys = new System.Collections.Generic.HashSet<string>(Config.DesyncResendKeys);
			Data.Reconciliation.ReconciliationService.StallSeconds = Config.DesyncStallSeconds;
			Data.Economy.EconomyRules.TravelFees = Config.TravelFees;
			Data.Economy.EconomyRules.MaxCarSalePrice = Config.MaxCarSalePrice;
			Data.Economy.EconomyRules.MaxCarPurchasePrice = Config.MaxCarPurchasePrice;
			Data.Presence.PresenceEvents.Left += Data.Reconciliation.ReconciliationService.OnLeft;
			Data.Jobs.JobsService.Initialize();
			Data.Cars.CarAwayRegistry.Initialize();
			Data.Presence.Rides.Initialize();
			Data.Cars.CarLocks.Initialize(Config.LockScope, Config.LockExpirySeconds);
			Data.Tools.ToolsStore.Initialize();
			Data.Garage.GarageLookService.Initialize();
			Data.Outdoor.OutdoorInstances.Configure(Config.SharedOutdoorScenes, Config.CarSelector, Config.OutdoorRejoinGraceSeconds, Config.OutdoorFillAllSpawnPoints);
			Data.Outdoor.OutdoorInstances.Initialize();
			Network.Handlers.VisualHandlers.Initialize();
			Network.Handlers.DriveHandlers.Initialize();
			Network.Handlers.PingHandlers.Initialize();
			Logger.CurrentLogLevel = Config.LogLevel;
			Logger.Info($"Log Level set to: {Logger.CurrentLogLevel}");
			
			GameDatabase.Initialize();
			if (!GameDatabase.isInitialized)
			{
				Logger.Error("Game Data Initialization failed. Closing..");
				Exit();
				return;
			}

			CompatibilityPolicy.Initialize(Config);
			SharedDlc.Changed += _ => AuthHandler.BroadcastServerInfo();
			PresenceEvents.Left += SharedDlc.Remove;

			try
			{
				GameDataManager.BackupCount = Config.BackupCount;
				GameDataManager.AutosaveIntervalSeconds = Config.AutosaveIntervalSeconds;
				SessionRegistry.Initialize(Assembly.GetExecutingAssembly());
				GameDataManager.LoadOrCreateSession();
			}
			catch (Exception ex)
			{
				Logger.Error($"Cannot start the session, the server will not start: {ex.Message}");
				Terminal.Gui.Application.Shutdown();
				Exit();
				return;
			}
			ConsoleCloseHandler.Install();

			string commandFile = GetArgument(args, "--command-file");
			if (commandFile != null)
				CommandFile.Configure(commandFile);

			Server.Start(Config.MaxPlayers, Config.Port);
			Diagnostics.Perf.PerfLog.Initialize(Config.PerfLogIntervalSeconds);
			Logger.Info($"Server started. Listening port {Config.Port}");
			
			Terminal.Gui.Application.Run(window);
			Terminal.Gui.Application.Shutdown();
			Exit();
		}

		private static int CheckSave(string path)
		{
			CMS21_Together_Core.Logging.Log.SetLogger(new ServerLoggerAdapter());
			try
			{
				GameDatabase.Initialize();
				SessionRegistry.Initialize(Assembly.GetExecutingAssembly());
				foreach (string line in GameDataManager.CheckSave(path))
					Console.WriteLine(line);
				Console.WriteLine("OK");
				return 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"FAILED: {ex.Message}");
				return 1;
			}
		}

		private static string GetArgument(string[] args, string name)
		{
			int index = Array.IndexOf(args, name);
			return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
		}

		private static void Exit()
		{
			Server.Stop();
			Logger.Info("Press Any key to exit..");
			Console.ReadKey();
		}
	}
}




