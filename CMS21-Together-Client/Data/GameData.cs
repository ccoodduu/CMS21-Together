using CMS.UI.Logic.Upgrades;
using UnityEngine;

namespace CMS21Together.Data;

public class GameData
{
	public static GameData Instance { get; private set; }
	
	public CharacterMotor LocalPlayer { get; private set; }
	public GarageAndToolsTab GarageTools { get; private set; }


	public static void Clear() => Instance = null;

	public GameData()
	{
		Instance = this;
		LocalPlayer = Object.FindObjectOfType<CharacterMotor>();
		GarageTools = Object.FindObjectOfType<GarageLevelManager>().garageAndToolsTab;
	}
}