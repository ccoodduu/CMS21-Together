using UnhollowerBaseLib;

namespace CMS21Together.Utils;

public static class SaveUtils
{
	private const int GameProfileCount = 4;
	private const int SessionProfileCount = 5;

	public static void ExtendProfileDataSize() => ResizeProfileData(SessionProfileCount);

	public static void RestoreProfileDataSize() => ResizeProfileData(GameProfileCount);

	private static void ResizeProfileData(int size)
	{
		var gameDataManager = Singleton<GameManager>.Instance.GameDataManager;
		var current = gameDataManager.ProfileData;
		if (current == null || current.Length == size)
			return;

		Il2CppReferenceArray<ProfileData> profileData = new(size);
		for (int i = 0; i < size && i < current.Length; i++)
			profileData[i] = current[i];

		gameDataManager.ProfileData = profileData;
	}
}
