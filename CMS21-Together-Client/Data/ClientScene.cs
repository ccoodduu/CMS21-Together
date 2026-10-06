using CMS21_Together_Core.Data.Enum;

namespace CMS21Together.Data;

public static class ClientScene
{
	public static GameScene LocalScene { get; set; } = GameScene.Menu;

	public static bool IsGarageReady => LocalScene == GameScene.Garage;

	public static GameScene FromSceneName(string sceneName)
	{
		switch ((sceneName ?? "").ToLowerInvariant())
		{
			case "garage": return GameScene.Garage;
			case "menu": return GameScene.Menu;
			default: return GameScene.Unknown;
		}
	}

	public static bool IsTransitionScene(string sceneName)
	{
		switch ((sceneName ?? "").ToLowerInvariant())
		{
			case "sceneloader":
			case "loadresources":
			case "introplayway":
				return true;
			default:
				return false;
		}
	}
}
