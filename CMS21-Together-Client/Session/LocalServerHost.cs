using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Session;

public enum HostState
{
	Idle,
	Starting,
	Running,
	Stopping,
	Failed
}

public class HostSettings
{
	public int Port = MainMod.PORT;
	public int MaxPlayers = MainMod.MAX_PLAYER;
	public string Password = "";
	public bool UseSteam = MainMod.IsSteamAvailable;
	public Gamemode Difficulty = Gamemode.Normal;
	public bool StartOver;
}

public static class LocalServerHost
{
	public const string ExeName = "CMS21_Together_Server.exe";
	private const string ProcessName = "CMS21_Together_Server";
	private const string ReadyLine = "Server started. Listening port";
	private const string GaveUpLine = "Press Any key to exit";
	private const float ReadyTimeout = 45f;
	private const float StopTimeout = 15f;
	private const int QuitTimeoutMs = 10000;
	private const float PollInterval = 0.5f;
	private const int LogTailLines = 20;

	private static Process process;
	private static HostSettings settings;
	private static float deadline;
	private static float nextPoll;

	public static HostState State { get; private set; } = HostState.Idle;
	public static string Message { get; private set; } = "";
	public static List<string> LogTail { get; private set; } = new List<string>();
	public static string AdminKey { get; private set; }
	public static int Port => settings?.Port ?? MainMod.PORT;
	public static string RunningServerPath { get; private set; }
	public static bool LeftoverRunning { get; private set; }

	public static string DefaultServerPath => Path.Combine(MelonUtils.GameDirectory, "TogetherServer", ExeName);

	public static string ServerPath =>
		string.IsNullOrWhiteSpace(PlayerSettings.ServerPath) ? DefaultServerPath : PlayerSettings.ServerPath.Trim().Trim('"');

	public static string CommandFilePath => Path.Combine(MelonUtils.UserDataDirectory, "CMS21Together", "host_commands.txt");

	public static bool HasSave(string serverPath)
	{
		try
		{
			return File.Exists(Path.Combine(ServerDir(serverPath), "Saves", "server_save.json"));
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool IsActive => State == HostState.Starting || State == HostState.Running || State == HostState.Stopping;

	private static string ServerDir(string serverPath) => Path.GetDirectoryName(Path.GetFullPath(serverPath));

	private static string LogPath(string serverPath) => Path.Combine(ServerDir(serverPath), "Log", "Latest.txt");

	public static bool Start(HostSettings hostSettings, out string error, string serverPath = null)
	{
		error = Refusal(hostSettings, serverPath ?? ServerPath, out string exe);
		if (error != null)
		{
			Message = error;
			Log.Warn($"[Host] Not starting: {error}");
			return false;
		}

		try
		{
			string dir = ServerDir(exe);
			if (hostSettings.StartOver) MoveSavesAside(dir);
			bool newSession = !HasSave(exe);

			DeleteIfExists(LogPath(exe));
			Directory.CreateDirectory(Path.GetDirectoryName(CommandFilePath));
			DeleteIfExists(CommandFilePath);
			DeleteIfExists(CommandFilePath + ".processing");

			AdminKey = Guid.NewGuid().ToString("N");
			var arguments = new List<string>
			{
				"--port", hostSettings.Port.ToString(),
				"--max-players", hostSettings.MaxPlayers.ToString(),
				"--use-steam", hostSettings.UseSteam.ToString(),
				"--password", hostSettings.Password ?? "",
				"--admin-key", AdminKey,
				"--command-file", CommandFilePath
			};
			if (newSession) arguments.AddRange(new[] { "--new-difficulty", hostSettings.Difficulty.ToString() });

			var startInfo = new ProcessStartInfo(exe)
			{
				Arguments = string.Join(" ", arguments.Select(Quote)),
				WorkingDirectory = dir,
				UseShellExecute = true,
				WindowStyle = ProcessWindowStyle.Minimized
			};
			Log.Info($"[Host] Starting {exe}: port {hostSettings.Port}, max players {hostSettings.MaxPlayers}, steam {hostSettings.UseSteam}, " +
			         $"password {(string.IsNullOrEmpty(hostSettings.Password) ? "none" : "set")}, {(newSession ? $"new session on {hostSettings.Difficulty}" : "continuing the saved session")}");

			process = Process.Start(startInfo);
			if (process == null) throw new InvalidOperationException("the server process did not start");
		}
		catch (Exception ex)
		{
			AdminKey = null;
			error = $"Could not start the server: {ex.Message}";
			Message = error;
			State = HostState.Failed;
			Log.Error($"[Host] {error}");
			return false;
		}

		settings = hostSettings;
		RunningServerPath = exe;
		LogTail = new List<string>();
		LeftoverRunning = false;
		State = HostState.Starting;
		Message = "Starting the server...";
		deadline = Time.realtimeSinceStartup + ReadyTimeout;
		nextPoll = 0f;
		return true;
	}

	private static string Refusal(HostSettings hostSettings, string serverPath, out string exe)
	{
		exe = null;
		if (IsActive) return "The server is already running or starting.";
		if (Network.Client.Instance.IsConnected || ConnectionStatus.IsBusy) return "Leave the current session first.";
		if (hostSettings.Port < 1 || hostSettings.Port > 65534) return $"Port {hostSettings.Port} is not valid.";
		if (hostSettings.MaxPlayers < 1) return "Allow at least one player.";
		if (hostSettings.Difficulty == Gamemode.Sandbox) return "Sandbox is not available for new sessions.";

		try
		{
			exe = Path.GetFullPath(serverPath);
		}
		catch (Exception)
		{
			return $"'{serverPath}' is not a valid path.";
		}
		if (!File.Exists(exe)) return $"The server program was not found at {exe}.";

		LeftoverRunning = FindServerProcesses(exe).Count > 0;
		if (LeftoverRunning) return $"A server from {exe} is already running. Stop it to host again.";
		if (!PortFree(hostSettings.Port)) return $"Port {hostSettings.Port} is in use by another program.";
		return null;
	}

	public static void Update()
	{
		if (!IsActive) return;
		float now = Time.realtimeSinceStartup;
		if (now < nextPoll) return;
		nextPoll = now + PollInterval;

		switch (State)
		{
			case HostState.Starting:
				PollStarting(now);
				break;
			case HostState.Stopping:
				PollStopping(now);
				break;
			case HostState.Running when process == null || process.HasExited:
				Stopped("The server stopped.");
				break;
		}
	}

	private static void PollStarting(float now)
	{
		if (process.HasExited)
		{
			Fail($"The server exited before it accepted connections (exit code {process.ExitCode}).");
			return;
		}

		string log = ReadLog(RunningServerPath);
		if (log.Contains(ReadyLine))
		{
			State = HostState.Running;
			Message = $"Server running on port {Port}.";
			Log.Info($"[Host] Server ready on port {Port}; joining.");
			Rejoin();
			return;
		}
		if (log.Contains(GaveUpLine))
		{
			Fail("The server could not start.");
			return;
		}
		if (now > deadline) Fail($"The server did not start within {ReadyTimeout:F0} s.");
	}

	private static void PollStopping(float now)
	{
		if (process == null || process.HasExited)
		{
			Stopped("Server stopped.");
			return;
		}
		if (now <= deadline) return;

		Log.Warn($"[Host] The server did not stop within {StopTimeout:F0} s; killing it.");
		Kill(process);
		Stopped("Server stopped (killed after the timeout).");
	}

	public static bool Rejoin()
	{
		if (State != HostState.Running) return false;
		if (JoinService.Join(JoinTarget.Ip("127.0.0.1", Port), out string error, settings.Password ?? "", AdminKey)) return true;
		Message = $"Server running on port {Port}, but joining failed: {error}";
		Log.Warn($"[Host] {Message}");
		return false;
	}

	public static string AdminKeyFor(JoinTarget target)
	{
		if (State != HostState.Running || AdminKey == null || target == null || target.Kind != JoinTargetKind.Ip || target.Port != Port) return null;
		return IsLocalHost(target.Host) ? AdminKey : null;
	}

	public static void Stop()
	{
		if (State == HostState.Starting)
		{
			Kill(process);
			Stopped("Server start cancelled.");
			return;
		}
		if (State != HostState.Running) return;

		LeaveOwnSession();
		if (!SendStop())
		{
			Kill(process);
			Stopped("Server stopped (killed, the stop command could not be written).");
			return;
		}
		State = HostState.Stopping;
		Message = "Stopping the server (saving)...";
		deadline = Time.realtimeSinceStartup + StopTimeout;
		nextPoll = 0f;
	}

	public static void StopLeftover(string serverPath = null)
	{
		string exe = Path.GetFullPath(serverPath ?? ServerPath);
		var leftovers = FindServerProcesses(exe);
		if (leftovers.Count == 0)
		{
			LeftoverRunning = false;
			Message = "No server is running.";
			return;
		}

		process = leftovers[0];
		RunningServerPath = exe;
		Log.Info($"[Host] Stopping the server left running from {exe}.");
		SendStop();
		State = HostState.Stopping;
		Message = "Stopping the server that was still running...";
		deadline = Time.realtimeSinceStartup + StopTimeout;
		nextPoll = 0f;
	}

	public static void StopOnQuit()
	{
		if (process == null || (State != HostState.Running && State != HostState.Stopping && State != HostState.Starting)) return;
		try
		{
			if (process.HasExited) return;
			Log.Info("[Host] Game is quitting; stopping the hosted server.");
			if (State != HostState.Stopping) SendStop();
			if (!process.WaitForExit(QuitTimeoutMs))
			{
				Log.Warn($"[Host] The server did not stop within {QuitTimeoutMs / 1000} s; killing it.");
				Kill(process);
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"[Host] Stopping the server on quit failed: {ex.Message}");
		}
	}

	private static void LeaveOwnSession()
	{
		var target = JoinService.CurrentTarget;
		bool own = target != null && target.Kind == JoinTargetKind.Ip && target.Port == Port && IsLocalHost(target.Host);
		if (own && (Network.Client.Instance.IsConnected || ConnectionStatus.State == JoinStatus.InSession)) JoinService.Leave();
	}

	private static bool SendStop()
	{
		try
		{
			File.AppendAllText(CommandFilePath, "/stop" + Environment.NewLine);
			return true;
		}
		catch (Exception ex)
		{
			Log.Warn($"[Host] Could not write the stop command to {CommandFilePath}: {ex.Message}");
			return false;
		}
	}

	private static void Fail(string reason)
	{
		Kill(process);
		LogTail = ReadLog(RunningServerPath).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
			.Where(l => l.Length > 0).Reverse().Take(LogTailLines).Reverse().ToList();
		AdminKey = null;
		State = HostState.Failed;
		Message = $"Hosting failed: {reason}";
		Log.Warn($"[Host] {Message} Server log tail:\n{string.Join("\n", LogTail)}");
	}

	private static void Stopped(string message)
	{
		AdminKey = null;
		process = null;
		LeftoverRunning = false;
		State = HostState.Idle;
		Message = message;
		Log.Info($"[Host] {message}");
	}

	private static void Kill(Process target)
	{
		try
		{
			if (target != null && !target.HasExited) target.Kill();
		}
		catch (Exception ex)
		{
			Log.Warn($"[Host] Could not kill the server process: {ex.Message}");
		}
	}

	private static void MoveSavesAside(string dir)
	{
		string saves = Path.Combine(dir, "Saves");
		if (!Directory.Exists(saves)) return;
		string target = Path.Combine(dir, $"Saves_old_{DateTime.Now:yyyyMMdd-HHmmss}");
		Directory.Move(saves, target);
		Log.Info($"[Host] Starting over: moved {saves} to {target}.");
	}

	private static void DeleteIfExists(string path)
	{
		if (File.Exists(path)) File.Delete(path);
	}

	private static string ReadLog(string serverPath)
	{
		try
		{
			using var stream = new FileStream(LogPath(serverPath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream);
			return reader.ReadToEnd();
		}
		catch (Exception)
		{
			return "";
		}
	}

	private static List<Process> FindServerProcesses(string exe)
	{
		var found = new List<Process>();
		foreach (var candidate in Process.GetProcessesByName(ProcessName))
		{
			try
			{
				if (string.Equals(Path.GetFullPath(candidate.MainModule.FileName), exe, StringComparison.OrdinalIgnoreCase)) found.Add(candidate);
			}
			catch (Exception)
			{
			}
		}
		return found;
	}

	private static bool PortFree(int port)
	{
		TcpListener tcp = null;
		UdpClient udp = null;
		try
		{
			tcp = new TcpListener(IPAddress.Any, port);
			tcp.Start();
			udp = new UdpClient(port);
			return true;
		}
		catch (SocketException)
		{
			return false;
		}
		finally
		{
			tcp?.Stop();
			udp?.Close();
		}
	}

	private static bool IsLocalHost(string host) =>
		host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));

	private static string Quote(string argument)
	{
		if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return argument;

		var quoted = new StringBuilder("\"");
		int backslashes = 0;
		foreach (char c in argument)
		{
			if (c == '\\')
			{
				backslashes++;
				continue;
			}
			if (c == '"') quoted.Append('\\', backslashes * 2 + 1);
			else quoted.Append('\\', backslashes);
			backslashes = 0;
			quoted.Append(c);
		}
		quoted.Append('\\', backslashes * 2);
		return quoted.Append('"').ToString();
	}
}
