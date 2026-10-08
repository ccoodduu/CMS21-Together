using System.Collections;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Logging;
using UnityEngine;

namespace CMS21Together.Logic.Outdoor;

public static class OutdoorArrival
{
	private const float CarsLoadedTimeoutSeconds = 30f;

	public static IEnumerator Apply()
	{
		if (!OutdoorSession.IsShared || OutdoorSession.Applied) yield break;
		var scene = OutdoorSession.Scene;
		if (scene != GameScene.Auction) yield return WaitForCars();

		if (OutdoorScenes.HasPiles(scene))
		{
			if (OutdoorSession.IsGenerator) LootSync.Record();
			else LootSync.ReplayIfNeeded();
		}
		if (scene == GameScene.Auction) AuctionSync.OnArrived();
		else OutdoorDigest.Send();
		OutdoorSession.MarkApplied();
	}

	public static IEnumerator UploadAsGenerator()
	{
		if (!OutdoorSession.IsShared || !OutdoorSession.IsGenerator) yield break;
		Log.Info($"[Outdoor] Taking over as generator of {OutdoorSession.Scene} #{OutdoorSession.InstanceId}.");
		if (OutdoorScenes.HasPiles(OutdoorSession.Scene) && OutdoorSession.Instance.Piles == null) LootSync.Record();
		if (OutdoorSession.Scene == GameScene.Auction) AuctionSync.OnArrived();
		else OutdoorDigest.Send();
	}

	private static IEnumerator WaitForCars()
	{
		float deadline = Time.realtimeSinceStartup + CarsLoadedTimeoutSeconds;
		while (Time.realtimeSinceStartup < deadline)
		{
			var created = OutdoorCarSync.CreatedIndices.Select(OutdoorCarSync.LoaderAt).Where(l => l != null).ToList();
			if (created.All(l => l.IsCarLoaded())) yield break;
			yield return null;
		}
		Log.Warn($"[Outdoor] Cars of {OutdoorSession.Scene} not all loaded after {CarsLoadedTimeoutSeconds:0} s; the digest may be incomplete.");
	}
}
