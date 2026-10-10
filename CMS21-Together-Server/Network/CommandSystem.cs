using System;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Placement;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network
{
	public static class CommandSystem
	{
		public static void Execute(string commandLine)
		{
			lock (GameDataManager.StateLock)
			{
				ExecuteLocked(commandLine);
			}
		}

		public static string Redact(string commandLine)
		{
			var match = System.Text.RegularExpressions.Regex.Match(commandLine, @"^(/?\s*password\s+set)\s+\S", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			return match.Success ? $"{match.Groups[1].Value} <redacted>" : commandLine;
		}

		private static void ExecuteLocked(string commandLine)
		{
			if (string.IsNullOrWhiteSpace(commandLine)) return;
			
			if (!commandLine.StartsWith("/"))
			{
				Logger.Warn("Commands must start with '/' (e.g. '/help').");
				return;
			}
			
			commandLine = commandLine.Substring(1).Trim();
			if (string.IsNullOrWhiteSpace(commandLine)) return;
			
			string[] args = commandLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string cmd = args[0].ToLower();

			switch (cmd)
			{
				case "help":
					Logger.Info("Available commands:");
					Logger.Info("  help              - Show this help message");
					Logger.Info("  exit / stop       - Stop the server");
					Logger.Info("  save              - Save the session now");
					Logger.Info("  compat            - Show game version, shared DLC, mod lists and last refusals");
					Logger.Info("  cars              - Show the cars, their revisions, part counts and claims");
					Logger.Info("  placement         - Show lifts, car places and parking slots");
					Logger.Info("  jobs              - Show orders, active jobs and the order generator");
					Logger.Info("  jobs expire <id>  - Expire an open order now");
					Logger.Info("  jobs reopen <id>  - Delete an active job's car and open the order again");
					Logger.Info("  cardetails <id>   - Show the stored details of a loader");
					Logger.Info("  away              - Show cars on a track, the test path or the dyno");
					Logger.Info("  records           - Show the race track lap and speed track top speed records");
					Logger.Info("  locks             - Show the part, fluid and car locks and the lock counters");
					Logger.Info("  tools             - Show the workshop machines, tool positions and claims");
					Logger.Info("  shoplist          - Show the shared shopping list");
					Logger.Info("  look              - Show the garage look (materials, texture pack, section count) and who is customising");
					Logger.Info("  outdoor [catalog|junkyard|barn|auction] - Show the shared outdoor instances, the car catalog or one scene");
					Logger.Info("  desync [check]    - Show recent desync repairs; check compares every player now");
					Logger.Info("  desync interval <s> - Seconds between automatic comparisons until the next restart");
					Logger.Info("  bugreport         - List the bug-report bundles in BugReports/");
					Logger.Info("  economy [n]       - Show the last n economy requests and a count per reason");
					Logger.Info("  economy cases     - Show opened cases that can still be looted");
					Logger.Info("  economy reasons   - Show the count per reason");
					Logger.Info("  gamemode <name>   - Set the difficulty (Normal, Expert, Sandbox, Easy) for everyone");
					Logger.Info("  kick <id>         - Kick a player by ID");
					Logger.Info("  password set <pw> - Set the DirectIP password until the server stops; password clear removes it");
					Logger.Info("  serverinfo        - Show the settings and each player's ping and admin flag");
					Logger.Info("  players           - Show the stored player records (identity, name, last seen, last place)");
					Logger.Info("  perf [top <n>|reset] - Show traffic per player and packet type, CPU, memory and handler times");
					Logger.Info("  money add <val>   - Add money");
					Logger.Info("  money set <val>   - Set money");
					Logger.Info("  level set <val>   - Set player level");
					Logger.Info("  exp set <val>     - Set player exp");
					break;

				case "exit":
				case "stop":
					Logger.Info("Stopping the server...");
					GameDataManager.SaveSession(true);
					Server.Stop();
					Environment.Exit(0);
					break;

				case "save":
					GameDataManager.RequestSave(true);
					break;

				case "compat":
					Logger.Info("Compatibility:");
					foreach (string line in CompatibilityPolicy.Describe())
						Logger.Info($"  {line}");
					break;

				case "cars":
					Logger.Info("Cars:");
					foreach (string line in CarPartsStore.Describe())
						Logger.Info($"  {line}");
					Logger.Info($"  {Handlers.CarPartsHandlers.DescribeCounters()}");
					break;

				case "desync":
					if (args.Length > 1 && args[1].ToLower() == "check")
					{
						Logger.Info("[Desync] Checking every player now.");
						Data.Reconciliation.ReconciliationService.Tick(Data.ServerTime.Time, force: true);
						break;
					}
					if (args.Length > 2 && args[1].ToLower() == "interval" && int.TryParse(args[2], out int interval) && interval > 0)
					{
						Data.Reconciliation.ReconciliationService.IntervalSeconds = interval;
						Logger.Info($"[Desync] Automatic comparisons every {interval} s.");
						break;
					}
					foreach (string line in Data.Reconciliation.ReconciliationService.Describe())
						Logger.Info(line);
					break;

				case "bugreport":
					foreach (string line in Data.Diagnostics.BugReportWriter.Describe())
						Logger.Info(line);
					break;

				case "locks":
					foreach (string line in CarLocks.Describe(Data.ServerTime.Time))
						Logger.Info($"[Locks] {line}");
					break;

				case "records":
					Logger.Info("Track records:");
					foreach (string line in Data.Tracks.TrackRecords.Describe())
						Logger.Info(line);
					break;

				case "away":
					Logger.Info("Away:");
					foreach (string line in CarAwayRegistry.Describe(Data.ServerTime.Time))
						Logger.Info(line);
					Logger.Info("Rides:");
					foreach (string line in Data.Presence.Rides.Describe(Data.ServerTime.Time))
						Logger.Info(line);
					break;

				case "economy":
					string economyArg = args.Length > 1 ? args[1].ToLower() : "";
					Logger.Info("Economy:");
					var economyLines = economyArg == "cases" ? Data.Economy.EconomyService.DescribeCases()
						: economyArg == "reasons" ? Data.Economy.EconomyService.DescribeReasons()
						: Data.Economy.EconomyService.Describe(int.TryParse(economyArg, out int economyCount) && economyCount > 0 ? economyCount : 10);
					foreach (string line in economyLines)
						Logger.Info(line);
					break;

				case "gamemode":
					if (args.Length > 1 && Enum.TryParse(args[1], true, out CMS21_Together_Core.Data.Enum.Gamemode gamemode))
					{
						var ws = GameDataManager.CurrentState?.WorldState;
						if (ws == null) { Logger.Warn("World State is not loaded yet."); break; }
						ws.Gamemode = gamemode;
						Logger.Success($"Gamemode is now {ws.Gamemode}");
						ws.updateGamemode = true;
						try { BroadcastWorldState(); }
						finally { ws.updateGamemode = false; }
					}
					else
					{
						Logger.Warn("Usage: gamemode Normal|Expert|Sandbox|Easy");
					}
					break;

				case "cardetails":
					if (args.Length > 1 && int.TryParse(args[1], out int detailsLoader)) Logger.Info($"[CarDetails] Loader {detailsLoader}: {CarDetailsStore.Describe(detailsLoader)}");
					break;

				case "jobs":
					if (args.Length > 2 && args[1].ToLower() == "expire")
					{
						if (int.TryParse(args[2], out int expireId) && Data.Jobs.JobsService.ExpireNow(expireId)) break;
						Logger.Warn($"No open order {args[2]} to expire.");
						break;
					}
					if (args.Length > 2 && args[1].ToLower() == "contributors")
					{
						foreach (string line in Data.Jobs.JobContributors.Describe(int.TryParse(args[2], out int contributorsId) ? contributorsId : -1))
							Logger.Info($"  {line}");
						break;
					}
					if (args.Length > 2 && args[1].ToLower() == "stats-to")
					{
						if (Data.Jobs.JobContributors.TryParseRule(args[2], out var rule))
						{
							Data.Jobs.JobContributors.Rule = rule;
							Logger.Info($"Job stats now go to: {rule.ToString().ToLowerInvariant()}.");
						}
						else Logger.Warn($"Unknown rule '{args[2]}'; use contributors, garage or finisher.");
						break;
					}
					if (args.Length > 2 && args[1].ToLower() == "reopen")
					{
						if (int.TryParse(args[2], out int reopenId) && Data.Jobs.JobsService.ReopenNow(reopenId)) break;
						Logger.Warn($"No active job {args[2]} to reopen.");
						break;
					}
					Logger.Info("Jobs:");
					foreach (string line in Data.Jobs.JobsService.Describe())
						Logger.Info($"  {line}");
					break;

				case "tools":
					Logger.Info("Tools:");
					foreach (string line in Data.Tools.ToolsStore.Describe())
						Logger.Info($"  {line}");
					break;

				case "look":
					Logger.Info("Garage look:");
					foreach (string line in Data.Garage.GarageLookService.Describe())
						Logger.Info($"  {line}");
					break;

				case "shoplist":
					Logger.Info("Shopping list:");
					foreach (string line in Data.ShopList.ShopListService.Describe())
						Logger.Info($"  {line}");
					break;

				case "outdoor":
					string outdoorArg = args.Length > 1 ? args[1].ToLower() : "";
					Logger.Info("Outdoor:");
					var outdoorLines = outdoorArg == "catalog" ? Data.Outdoor.CarCatalog.Describe()
						: Enum.TryParse(outdoorArg, true, out CMS21_Together_Core.Data.Enum.GameScene outdoorScene) && CMS21_Together_Core.Data.Outdoor.OutdoorScenes.IsOutdoor(outdoorScene)
							? Data.Outdoor.OutdoorInstances.DescribeScene(outdoorScene)
							: Data.Outdoor.OutdoorInstances.Describe();
					foreach (string line in outdoorLines)
						Logger.Info($"  {line}");
					break;

				case "placement":
					Logger.Info("Placement:");
					foreach (string line in PlacementRules.Describe())
						Logger.Info($"  {line}");
					break;

				case "kick":
					if (args.Length > 1 && int.TryParse(args[1], out int playerId))
						Server.Kick(playerId, "server command");
					else
						Logger.Warn("Usage: kick <id>");
					break;

				case "password":
					if (args.Length > 2 && args[1].ToLower() == "set")
					{
						Program.Config.SetPassword(string.Join(" ", args, 2, args.Length - 2));
						Logger.Success("Password set for new DirectIP joins.");
						Handlers.AuthHandler.BroadcastServerInfo();
					}
					else if (args.Length > 1 && args[1].ToLower() == "clear")
					{
						Program.Config.SetPassword("");
						Logger.Success("Password removed.");
						Handlers.AuthHandler.BroadcastServerInfo();
					}
					else
					{
						Logger.Warn("Usage: password set <pw> OR password clear");
					}
					break;

				case "serverinfo":
					Logger.Info($"Settings: {Program.Config.Describe()}");
					foreach (var client in Server.Clients.Values)
					{
						if (!client.IsConnected) continue;
						var record = Data.Presence.PresenceRegistry.Get(client.ID);
						Logger.Info($"  Client[{client.ID}] '{record?.Username ?? "?"}' {client.ConnectionType}, {client.SyncState}, RTT {client.RttMs:F0} ms, admin {client.IsAdmin}");
					}
					break;

				case "perf":
					string perfArg = args.Length > 1 ? args[1].ToLower() : "";
					if (perfArg == "reset")
					{
						Diagnostics.Perf.PerfSummary.Reset(byCommand: true);
						Logger.Info("[Perf] Counters reset.");
						break;
					}
					var perfLines = perfArg == "top"
						? Diagnostics.Perf.PerfSummary.DescribeTop(args.Length > 2 && int.TryParse(args[2], out int perfTop) && perfTop > 0 ? perfTop : 10)
						: Diagnostics.Perf.PerfSummary.Describe();
					foreach (string line in perfLines)
						Logger.Info($"[Perf] {line}");
					break;

				case "players":
					Logger.Info("Players:");
					foreach (string line in Data.Presence.PlayerRecords.DescribeAll())
						Logger.Info($"  {line}");
					break;

				case "money":
					if (args.Length > 2 && int.TryParse(args[2], out int moneyVal))
					{
						var ws = GameDataManager.CurrentState?.WorldState;
						if (ws == null) { Logger.Warn("World State is not loaded yet."); break; }

						if (args[1].ToLower() == "add") ws.Money += moneyVal;
						else if (args[1].ToLower() == "set") ws.Money = moneyVal;
						else { Logger.Warn("Usage: money add <val> OR money set <val>"); break; }
						
						Logger.Success($"Money is now {ws.Money}$");
						BroadcastWorldState();
					}
					else
					{
						Logger.Warn("Usage: money add <val> OR money set <val>");
					}
					break;

				case "level":
					if (args.Length > 2 && args[1].ToLower() == "set" && int.TryParse(args[2], out int levelVal))
					{
						var ws = GameDataManager.CurrentState?.WorldState;
						if (ws == null) { Logger.Warn("World State is not loaded yet."); break; }

						ws.Level = levelVal;
						Logger.Success($"Level is now {ws.Level}");
						BroadcastWorldState();
					}
					else
					{
						Logger.Warn("Usage: level set <val>");
					}
					break;

				case "exp":
					if (args.Length > 2 && args[1].ToLower() == "set" && int.TryParse(args[2], out int expVal))
					{
						var ws = GameDataManager.CurrentState?.WorldState;
						if (ws == null) { Logger.Warn("World State is not loaded yet."); break; }

						ws.Exp = expVal;
						Logger.Success($"Exp is now {ws.Exp}");
						BroadcastWorldState();
					}
					else
					{
						Logger.Warn("Usage: exp set <val>");
					}
					break;

				default:
					Logger.Warn($"Unknown command: {cmd}. Type 'help' for a list of commands.");
					break;
			}
		}

		private static void BroadcastWorldState()
		{
			if (GameDataManager.CurrentState != null && GameDataManager.CurrentState.WorldState != null)
			{
				Server.SendToClients(GameDataManager.CurrentState.WorldState);
			}
		}
	}
}
