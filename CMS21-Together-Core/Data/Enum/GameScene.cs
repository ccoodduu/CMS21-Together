namespace CMS21_Together_Core.Data.Enum;

public enum GameScene
{
	Unknown = 0,
	Loading = 1,
	Menu = 2,
	Garage = 3,
	Parking = 4,
	Junkyard = 5,
	Barn = 6,
	Auction = 7,
	Salon = 8,
	Showroom = 9,
	TestTrack = 10,
	RaceTrack = 11,
	FunTrack = 12,
	OffroadTrack = 13,
	DragStrip = 14,
	CustomTrack = 15,
	SpeedTrack = 16,
	PhotoLocation = 17,
	Tutorial = 18
}

public static class GameSceneInfo
{
	public static bool ShowsAvatars(GameScene scene)
	{
		switch (scene)
		{
			case GameScene.Unknown:
			case GameScene.Loading:
			case GameScene.Menu:
			case GameScene.Tutorial:
			case GameScene.Barn:
				return false;
			default:
				return true;
		}
	}
}
