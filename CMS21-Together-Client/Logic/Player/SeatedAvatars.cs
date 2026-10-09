using CMS21Together.Data;
using UnityEngine;

namespace CMS21Together.Logic.Player;

public static class SeatedAvatars
{
	private const float RetryInterval = 0.5f;

	private static float nextRetry;

	public static void LateUpdate()
	{
		if (!ClientScene.IsGarageReady || PresenceManager.Roster.Count == 0) return;
		bool retry = Time.realtimeSinceStartup >= nextRetry;
		if (retry) nextRetry = Time.realtimeSinceStartup + RetryInterval;
		foreach (var player in PresenceManager.Roster.Values)
		{
			if (!SeatPoses.IsSeatedInGarage(player.Record)) continue;
			if (!SeatPoses.TryGetGarage(player.Record, out var position, out var rotation))
			{
				if (player.HasAvatar && player.Avatar.gameObject.activeSelf) player.Avatar.gameObject.SetActive(false);
				continue;
			}
			if (!player.HasAvatar)
			{
				if (retry) PresenceManager.Reconcile(player.Record.PlayerId);
				continue;
			}
			SeatPoses.Place(player.Avatar, position, rotation);
		}
	}
}
