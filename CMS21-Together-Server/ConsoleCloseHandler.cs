using System.Runtime.InteropServices;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server
{
	public static class ConsoleCloseHandler
	{
		private delegate bool HandlerRoutine(int ctrlType);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);

		private static HandlerRoutine handler;

		public static void Install()
		{
			handler = OnConsoleEvent;
			if (!SetConsoleCtrlHandler(handler, true))
				Logger.Warn("Could not install the console close handler; closing the window will not save.");
		}

		private static bool OnConsoleEvent(int ctrlType)
		{
			Logger.Info($"Console event {ctrlType}, saving before exit.");
			GameDataManager.SaveSession();
			return false;
		}
	}
}
