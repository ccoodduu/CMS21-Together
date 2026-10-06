using System;
using CMS21_Together_Core.Logging;

namespace CMS21Together.Guard;

public static class GuardNotice
{
	public const string Title = "Multiplayer";

	public static string LastText { get; private set; }

	public static void Forget() => LastText = null;

	public static void Show(string text)
	{
		LastText = text;
		try
		{
			var ui = UIManager.Get();
			if (ui != null) ui.ShowPopup(Title, text, PopupType.Normal);
		}
		catch (Exception ex)
		{
			Log.Warn($"[Guard] Could not show the message '{text}': {ex.Message}");
		}
	}
}
