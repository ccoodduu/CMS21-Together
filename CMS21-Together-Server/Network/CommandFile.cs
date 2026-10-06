using System;
using System.IO;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network
{
	public static class CommandFile
	{
		private const float PollInterval = 0.25f;

		private static string path;
		private static float lastPoll;

		public static void Configure(string commandFilePath)
		{
			path = Path.GetFullPath(commandFilePath);
			Logger.Info($"Reading commands from {path}");
		}

		public static void Poll(float now)
		{
			if (path == null || now - lastPoll < PollInterval) return;
			lastPoll = now;

			string[] lines;
			string processingPath = path + ".processing";
			try
			{
				if (!File.Exists(path)) return;
				if (File.Exists(processingPath)) File.Delete(processingPath);
				File.Move(path, processingPath);
				lines = File.ReadAllLines(processingPath);
				File.Delete(processingPath);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				return;
			}

			foreach (string raw in lines)
			{
				string line = raw.Trim();
				if (line.Length == 0) continue;
				if (!line.StartsWith("/")) line = "/" + line;
				Logger.Info($"[CommandFile] {CommandSystem.Redact(line)}");
				CommandSystem.Execute(line);
			}
		}
	}
}
