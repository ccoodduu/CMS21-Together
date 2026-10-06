using System;
using MelonLoader;
using CoreLog = CMS21_Together_Core.Logging;

namespace CMS21Together.Logging
{
	// Goes through MelonLogger so every line lands in MelonLoader's Latest.log; ModConsole picks the mod's own
	// lines up from MelonLogger's callbacks for its window.
	public class ClientLoggerAdapter : CoreLog.ILogger
	{
		public void Debug(string message) => MelonLogger.Msg(ConsoleColor.DarkGray, message);
		public void Info(string message) => MelonLogger.Msg(message);
		public void Warn(string message) => MelonLogger.Warning(message);
		public void Error(string message) => MelonLogger.Error(message);
		public void Success(string message) => MelonLogger.Msg(ConsoleColor.Green, message);
	}
}
