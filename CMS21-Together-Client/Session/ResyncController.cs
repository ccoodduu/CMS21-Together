using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;
using CMS21Together.Guard;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using CMS21Together.UI;
using UnityEngine;

namespace CMS21Together.Session;

// Resync = reload the garage, which runs the full late-join snapshot (desync-detection-and-resync D4).
public static class ResyncController
{
	private const float CooldownSeconds = 30f;

	private static float lastResync = -CooldownSeconds;

	public static string Request(bool force = false)
	{
		string refusal = Refusal(force);
		if (refusal != null)
		{
			Log.Info($"[Resync] Refused: {refusal}");
			ModNotify.ShowToast(refusal);
			return refusal;
		}

		lastResync = Time.realtimeSinceStartup;
		Log.Info("[Resync] Reloading the garage from the server.");
		ModNotify.ShowToast("Resyncing: reloading the garage from the server…");
		var center = NotificationCenter.m_instance;
		using (FeatureGuard.Bypass())
			center.StartCoroutine(center.SelectSceneToLoad("garage", SceneType.Garage, true, false));
		return null;
	}

	private static string Refusal(bool force)
	{
		if (Client.Instance == null || !Client.Instance.IsConnectionValid) return "Resync works only while connected.";
		if (ClientScene.LocalScene != GameScene.Garage) return "Resync works only in the garage.";
		if (!ClientData.IsInitialSyncFinished || SyncTracker.InSnapshot) return "Still syncing with the server.";
		if (CarPartsSync.HasOwnBaselinePending) return "Wait until your new car has finished loading.";
		float wait = lastResync + CooldownSeconds - Time.realtimeSinceStartup;
		if (!force && wait > 0f) return $"Wait {Mathf.CeilToInt(wait)} s before the next resync.";
		return null;
	}
}
