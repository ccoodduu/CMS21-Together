using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.Session;

namespace CMS21Together.UI;

public static class MultiplayerMenuModel
{
	public static bool JoinPanelOpen { get; private set; }
	public static string TargetText { get; set; } = "";
	public static string NameText { get; set; } = "";
	public static string PanelError { get; private set; } = "";

	public static bool NameEditable => !Client.Instance.IsConnected;

	public static void OpenJoinPanel()
	{
		TargetText = PlayerSettings.LastJoinTarget;
		NameText = PlayerSettings.PlayerName;
		PanelError = "";
		JoinPanelOpen = true;
	}

	public static void Close()
	{
		JoinPanelOpen = false;
	}

	public static void Join()
	{
		if (NameEditable && NameText != PlayerSettings.PlayerName) PlayerSettings.PlayerName = NameText.Trim();

		if (JoinService.Join(TargetText, out string error))
		{
			PanelError = "";
			JoinPanelOpen = false;
		}
		else
		{
			PanelError = error;
		}
	}

	public static void AcknowledgeMessage()
	{
		ConnectionStatus.MessagePending = false;
		ConnectionStatus.Set(JoinStatus.Idle);
	}
}
