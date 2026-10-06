using System;
using System.Linq;
using CMS.Extensions;
using CMS.UI.Controls;
using CMS.UI.Windows;
using HarmonyLib;

namespace CMS21Together.Guard;

[HarmonyPatch]
public static class PauseMenuHooks
{
	private static readonly string[] SaveCaptionKeys = { "pie_settings_save", "GUI_Pause_QuitMenuSaveButton" };

	[HarmonyPatch(typeof(PauseQuitWindow), nameof(PauseQuitWindow.CreateButton))]
	[HarmonyPostfix]
	private static void HideSaveButtons(GenericButtonOutline button)
	{
		if (button == null || button.text == null) return;
		string caption = button.text.text ?? "";
		bool isSave = SaveCaptionKeys.Any(key => string.Equals(caption.Trim(), StringExtension.Localize(key).Trim(), StringComparison.OrdinalIgnoreCase));
		if (!isSave) return;
		button.gameObject.SetActive(!FeatureGuard.IsSessionActive);
	}
}
