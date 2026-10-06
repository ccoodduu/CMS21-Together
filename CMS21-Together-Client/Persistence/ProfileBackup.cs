using System;
using System.IO;
using System.Linq;
using CMS21_Together_Core.Logging;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Persistence;

public static class ProfileBackup
{
	private const int KeepCount = 5;
	private static readonly string[] ProfileFolders = { "Save", "Backup" };

	private static bool doneThisRun;

	public static string BackupRoot => Path.Combine(MelonUtils.UserDataDirectory, "CMS21Together", "ProfileBackups");

	public static bool EnsureBackedUp()
	{
		if (doneThisRun) return true;

		try
		{
			string source = Application.persistentDataPath;
			string target = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
			int copied = 0;

			foreach (string folder in ProfileFolders)
			{
				string sourceFolder = Path.Combine(source, folder);
				if (!Directory.Exists(sourceFolder)) continue;

				string targetFolder = Path.Combine(target, folder);
				Directory.CreateDirectory(targetFolder);
				foreach (string file in Directory.GetFiles(sourceFolder))
				{
					File.Copy(file, Path.Combine(targetFolder, Path.GetFileName(file)), false);
					copied++;
				}
			}
			Directory.CreateDirectory(target);

			foreach (var old in new DirectoryInfo(BackupRoot).GetDirectories().OrderByDescending(d => d.Name).Skip(KeepCount))
				old.Delete(true);

			doneThisRun = true;
			Log.Info($"[ProfileBackup] Backed up {copied} profile files from {source} to {target}.");
			if (File.Exists(Path.Combine(source, "Save", "profile4.cms21b")))
				Log.Warn("[ProfileBackup] profile4.cms21b exists (written by an older mod version); it is kept as is.");
			return true;
		}
		catch (Exception ex)
		{
			Log.Error($"[ProfileBackup] Could not back up the game profiles, join aborted: {ex.Message}");
			return false;
		}
	}
}
