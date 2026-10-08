using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Away;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using CMS21Together.UI;
using UnityEngine;
using VehiclePhysics;
using Object = UnityEngine.Object;

namespace CMS21Together.Logic.Driving;

public static class RideAlong
{
	public const float CarWaitSeconds = 90f;
	public const float CarLostSeconds = 10f;
	public const float ReturnSettleSeconds = 20f;
	private const float SeatedAvatarDrop = 0.45f;
	private const float LookDegreesPerUnit = 2f;
	private const float MaxLookYaw = 150f;
	private const float MaxLookPitch = 70f;
	private const float FreezeInterval = 0.5f;
	private const float AvatarRetryInterval = 0.5f;

	public enum Phase { None, Travelling, WaitingForCar, Seated, Returning }

	public class Ride
	{
		public int DriverId;
		public int PassengerId;
		public int Loader;
		public string PassengerName;
	}

	private static readonly Dictionary<int, Ride> rides = new Dictionary<int, Ride>();
	private static bool subscribed;
	private static float phaseSince;
	private static float carLostSince = -1f;
	private static bool endRequested;
	private static float nextFreeze;
	private static float nextAvatarRetry;
	private static PrepareCarPhysics frozen;
	private static bool ownHidden;
	private static Vector3 headOffset = new Vector3(0f, 0.85f, 0f);
	private static float lookYaw;
	private static float lookPitch;
	private static bool lookAxes = true;
	private static CameraController cameraController;

	public static Phase Current { get; private set; }
	public static int DriverId { get; private set; } = -1;
	public static int Loader { get; private set; } = -1;
	public static string DriverName { get; private set; }
	public static string EndMessage { get; private set; }
	public static bool HeadMeasured { get; private set; }
	public static int HiddenRenderers { get; private set; }
	public static bool CameraPlaced { get; private set; }
	public static Vector3 CameraTarget { get; private set; }
	public static int CameraFrames { get; private set; }
	public static float CameraDrift { get; private set; }
	public static float MaxCameraDrift { get; private set; }

	public static IEnumerable<Ride> All => rides.Values;
	public static bool IsPassenger => Current != Phase.None;

	private static bool Connected => Client.Instance != null && Client.Instance.IsConnectionValid && ClientData.IsInitialSyncFinished;

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		ClientScene.LeavingScene += OnLeavingScene;
	}

	public static void Reset()
	{
		rides.Clear();
		ReleaseCamera();
		Clear();
	}

	private static void Clear()
	{
		Current = Phase.None;
		DriverId = -1;
		Loader = -1;
		endRequested = false;
		carLostSince = -1f;
		frozen = null;
		ownHidden = false;
		HiddenRenderers = 0;
		HeadMeasured = false;
		cameraController = null;
	}

	private static void SetPhase(Phase phase)
	{
		Current = phase;
		phaseSince = Time.realtimeSinceStartup;
		Log.Info($"[Ride] Passenger phase {phase} (driver {DriverId}, car {Loader}).");
	}

	private static string Name(int playerId) => CarAwaySync.OwnerName(playerId);

	public static void OnUpdate(RideUpdatePacket packet)
	{
		if (packet == null || Client.Instance == null) return;
		int me = Client.Instance.ID;
		if (packet.Active)
		{
			var ride = new Ride { DriverId = packet.DriverId, PassengerId = packet.PassengerId, Loader = packet.CarLoaderID, PassengerName = Name(packet.PassengerId) };
			rides[packet.PassengerId] = ride;
			Log.Info($"[Ride] Player {packet.PassengerId} rides along with player {packet.DriverId} in car {packet.CarLoaderID}.");
			if (packet.PassengerId == me) Begin(packet);
			else if (packet.DriverId == me) ModNotify.ShowToast($"{ride.PassengerName} rides along.");
		}
		else
		{
			string passengerName = rides.TryGetValue(packet.PassengerId, out var ended) ? ended.PassengerName : Name(packet.PassengerId);
			rides.Remove(packet.PassengerId);
			Log.Info($"[Ride] Player {packet.PassengerId}'s ride with player {packet.DriverId} ended ({packet.Reason}).");
			if (packet.PassengerId == me) End(packet.Reason, packet.DriverId);
			else if (packet.DriverId == me && packet.Reason == RideEndReason.PassengerLeft) ModNotify.ShowToast($"{passengerName} left the ride.");
			else if (packet.DriverId == me && packet.Reason == RideEndReason.PassengerDidNotArrive) ModNotify.ShowToast($"{passengerName} could not ride along.");
		}
		PresenceManager.Reconcile(packet.PassengerId);
		PresenceManager.Reconcile(packet.DriverId);
	}

	private static void Begin(RideUpdatePacket packet)
	{
		if (Current != Phase.None)
		{
			Log.Warn($"[Ride] Already riding with player {DriverId}; the ride with player {packet.DriverId} waits for the server's timeout.");
			return;
		}
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(packet.CarLoaderID);
		if (ClientScene.LocalScene != GameScene.Garage || carLoader == null || !carLoader.IsCarLoaded())
		{
			Log.Warn($"[Ride] Cannot travel with player {packet.DriverId}: scene {ClientScene.LocalScene}, car {packet.CarLoaderID} {(carLoader != null && carLoader.IsCarLoaded() ? "loaded" : "not loaded")}.");
			return;
		}

		DriverId = packet.DriverId;
		DriverName = Name(packet.DriverId);
		Loader = packet.CarLoaderID;
		EndMessage = null;
		endRequested = false;
		lookYaw = 0f;
		lookPitch = 0f;
		CameraFrames = 0;
		CameraDrift = 0f;
		MaxCameraDrift = 0f;
		SetPhase(Phase.Travelling);
		GlobalData.SelectedCarLoader = carLoader.gameObject.name;
		GlobalData.TestToShow = "";
		GlobalData.NewMileage = 0;
		ModNotify.ShowToast($"Riding along with {DriverName}.");
		var center = NotificationCenter.m_instance;
		center.StartCoroutine(center.SelectSceneToLoad("Test_track_1", SceneType.TestTrack, true, true));
	}

	private static void End(RideEndReason reason, int driverId)
	{
		if (Current == Phase.None || driverId != DriverId) return;
		EndMessage = reason switch
		{
			RideEndReason.DriverReturned => $"{DriverName} drove back to the garage.",
			RideEndReason.DriverLeft => $"{DriverName} left the game.",
			RideEndReason.DriveCancelled => $"{DriverName}'s test drive ended.",
			RideEndReason.PassengerDidNotArrive => "You did not reach the test track in time.",
			_ => null,
		};
		if (Current == Phase.Returning) return;
		if (ClientScene.LocalScene == GameScene.Garage)
		{
			if (EndMessage != null) ModNotify.ShowToast(EndMessage);
			Clear();
			return;
		}
		endRequested = true;
	}

	private static void OnLeavingScene(GameScene from, GameScene to)
	{
		if (Current == Phase.None || from != GameScene.TestTrack) return;
		GlobalData.NewMileage = 0;
		GlobalData.TestToShow = "";
		GlobalData.SelectedCarLoader = "";
		ReleaseCamera();
		frozen = null;
		if (Current != Phase.Returning) SetPhase(Phase.Returning);
	}

	public static void Update()
	{
		if (Current == Phase.None) return;
		if (!Connected)
		{
			ReleaseCamera();
			Clear();
			return;
		}
		MeasureDrift();
		float inPhase = Time.realtimeSinceStartup - phaseSince;
		switch (Current)
		{
			case Phase.Travelling:
				if (ClientScene.LocalScene == GameScene.TestTrack) SetPhase(Phase.WaitingForCar);
				else if (ClientScene.LocalScene == GameScene.Garage && inPhase > 5f)
				{
					Log.Warn("[Ride] The trip to the test track did not start.");
					Clear();
				}
				break;
			case Phase.WaitingForCar:
			case Phase.Seated:
				if (ClientScene.LocalScene != GameScene.TestTrack) break;
				bool ownReady = Freeze();
				if (endRequested)
				{
					if (ownReady || inPhase > ReturnSettleSeconds) Return();
					break;
				}
				var copy = RemoteCars.Of(DriverId);
				bool copyShown = copy != null && copy.Ready && copy.Root.gameObject.activeInHierarchy;
				if (Current == Phase.WaitingForCar)
				{
					if (copyShown && ownReady) Seat();
					else if (inPhase > CarWaitSeconds)
					{
						EndMessage = $"{DriverName}'s car did not appear.";
						Return();
					}
				}
				else if (!copyShown)
				{
					if (carLostSince < 0f) carLostSince = Time.realtimeSinceStartup;
					else if (Time.realtimeSinceStartup - carLostSince > CarLostSeconds)
					{
						EndMessage = $"{DriverName}'s car is gone.";
						Return();
					}
				}
				else carLostSince = -1f;
				break;
			case Phase.Returning:
				if (ClientScene.LocalScene == GameScene.Garage)
				{
					Log.Info($"[Ride] Back in the garage after riding with player {DriverId}.");
					Clear();
				}
				break;
		}
	}

	private static void Seat()
	{
		MeasureHead(frozen);
		HideOwnCar(frozen);
		carLostSince = -1f;
		SetPhase(Phase.Seated);
		Log.Info($"[Ride] Seated in player {DriverId}'s car (head offset ({headOffset.x:F2}, {headOffset.y:F2}, {headOffset.z:F2}), measured {HeadMeasured}, {HiddenRenderers} renderers of the own track car hidden).");
	}

	private static void Return()
	{
		SetPhase(Phase.Returning);
		ReleaseCamera();
		if (EndMessage != null) ModNotify.ShowToast(EndMessage);
		GlobalData.NewMileage = 0;
		GlobalData.TestToShow = "";
		var manager = Object.FindObjectOfType<TestTrackManager>();
		if (manager != null)
		{
			manager.ReturnToGarage();
			return;
		}
		var center = NotificationCenter.m_instance;
		center.StartCoroutine(center.SelectSceneToLoad("garage", SceneType.Garage, true, true));
	}

	private static bool Freeze()
	{
		var physics = PrepareCarPhysics.Get();
		var carLoader = physics == null ? null : physics.CarLoader;
		var vehicle = physics == null ? null : physics.VehicleController;
		if (carLoader == null || !carLoader.IsCarLoaded() || vehicle == null || !vehicle.initialized) return false;
		if (frozen == physics && Time.realtimeSinceStartup < nextFreeze) return true;
		nextFreeze = Time.realtimeSinceStartup + FreezeInterval;
		if (frozen != physics)
		{
			frozen = physics;
			ownHidden = false;
			Log.Info("[Ride] Own track car frozen: the passenger does not drive.");
		}
		physics.EnableKinematic(true);
		physics.EnableGasPedal(false);
		physics.EnableAudio(false, false);
		foreach (var input in vehicle.GetComponentsInChildren<VPStandardInput>(true))
			if (input.enabled) input.enabled = false;
		if (Current == Phase.Seated && !ownHidden) HideOwnCar(physics);
		return true;
	}

	private static void HideOwnCar(PrepareCarPhysics physics)
	{
		if (physics == null || !physics || physics.VehicleController == null) return;
		int hidden = 0;
		foreach (var renderer in physics.VehicleController.GetComponentsInChildren<Renderer>(true))
		{
			if (!renderer.enabled) continue;
			renderer.enabled = false;
			hidden++;
		}
		HiddenRenderers += hidden;
		ownHidden = true;
	}

	private static void MeasureHead(PrepareCarPhysics physics)
	{
		HeadMeasured = false;
		var carLoader = physics == null ? null : physics.CarLoader;
		if (carLoader == null) return;
		bool rightHandDrive = carLoader.OtherData.RightHandDrive;
		var seat = rightHandDrive ? carLoader.GetRightSeatHandle() : carLoader.GetLeftSeatHandle();
		var controller = Controller();
		var head = controller != null && controller.vpCamera != null && controller.vpCamera.attachTo != null ? controller.vpCamera.attachTo.attachTarget : null;
		if (head == null || !head) head = physics.headInside;
		if (seat != null && seat && head != null && head && CarFrame(carLoader, out var frame, out _))
		{
			var local = Quaternion.Inverse(frame) * (head.position - seat.transform.position);
			local.x = -local.x; // the own track car is the same car: its driver head, mirrored to the passenger seat
			headOffset = local;
			HeadMeasured = true;
			return;
		}
		var interior = carLoader.InteriorParams;
		headOffset = new Vector3(0f, interior.SeatScale * 0.9f * (rightHandDrive ? interior.SeatLeftHeightMod : interior.SeatRightHeightMod), 0f);
	}

	public static bool CarFrame(CarLoader carLoader, out Quaternion frame, out Vector3 center)
	{
		frame = Quaternion.identity;
		center = Vector3.zero;
		if (carLoader == null || !carLoader) return false;
		var fl = carLoader.GetWheelFLHandle();
		var fr = carLoader.GetWheelFRHandle();
		var rl = carLoader.GetWheelRLHandle();
		var rr = carLoader.GetWheelRRHandle();
		if (fl == null || !fl || fr == null || !fr || rl == null || !rl || rr == null || !rr) return false;
		Vector3 pfl = fl.transform.position, pfr = fr.transform.position, prl = rl.transform.position, prr = rr.transform.position;
		var front = (pfl + pfr - prl - prr) * 0.5f;
		var right = (pfr + prr - pfl - prl) * 0.5f;
		if (front.sqrMagnitude < 0.01f || right.sqrMagnitude < 0.01f) return false;
		frame = Quaternion.LookRotation(front, Vector3.Cross(front, right));
		center = (pfl + pfr + prl + prr) * 0.25f;
		return true;
	}

	public static bool SeatPose(CarLoader carLoader, bool passenger, out Vector3 seat, out Quaternion frame)
	{
		seat = Vector3.zero;
		frame = Quaternion.identity;
		if (carLoader == null || !carLoader || !CarFrame(carLoader, out frame, out _)) return false;
		bool left = passenger == carLoader.OtherData.RightHandDrive;
		var handle = left ? carLoader.GetLeftSeatHandle() : carLoader.GetRightSeatHandle();
		if (handle == null || !handle) return false;
		seat = handle.transform.position;
		return true;
	}

	public static CarLoader OwnTrackCar()
	{
		if (ClientScene.LocalScene != GameScene.TestTrack || IsPassenger) return null;
		var physics = PrepareCarPhysics.Get();
		var carLoader = physics == null ? null : physics.CarLoader;
		return carLoader != null && carLoader.IsCarLoaded() ? carLoader : null;
	}

	public static CarLoader CopyOf(int driverId)
	{
		if (ClientScene.LocalScene != GameScene.TestTrack) return null;
		var copy = RemoteCars.Of(driverId);
		return copy != null && copy.Ready && copy.Root.gameObject.activeInHierarchy ? copy.Loader : null;
	}

	public static bool TryGetPassengerHead(out Vector3 head, out Quaternion frame)
	{
		head = Vector3.zero;
		frame = Quaternion.identity;
		if (Current != Phase.Seated || !SeatPose(CopyOf(DriverId), true, out var seat, out frame)) return false;
		head = seat + frame * headOffset;
		return true;
	}

	public static bool TryGetAvatarSeat(int playerId, out Vector3 position, out Quaternion rotation)
	{
		position = Vector3.zero;
		rotation = Quaternion.identity;
		if (ClientScene.LocalScene != GameScene.TestTrack || Client.Instance == null) return false;
		int me = Client.Instance.ID;
		CarLoader carLoader = null;
		bool passenger = false;
		if (rides.TryGetValue(playerId, out var ride))
		{
			passenger = true;
			carLoader = ride.DriverId == me ? OwnTrackCar() : CopyOf(ride.DriverId);
		}
		else if (rides.Values.Any(r => r.DriverId == playerId))
		{
			carLoader = CopyOf(playerId);
		}
		if (!SeatPose(carLoader, passenger, out var seat, out rotation)) return false;
		position = seat - rotation * Vector3.up * SeatedAvatarDrop;
		return true;
	}

	public static void LateUpdate()
	{
		if (ClientScene.LocalScene != GameScene.TestTrack || rides.Count == 0 && Current == Phase.None) return;
		PlaceCamera();
		PlaceAvatars();
	}

	private static CameraController Controller()
	{
		if (cameraController == null || !cameraController) cameraController = Object.FindObjectOfType<CameraController>();
		return cameraController;
	}

	private static void PlaceCamera()
	{
		if (!TryGetPassengerHead(out var head, out var frame)) return;
		var camera = Camera.main;
		if (camera == null) return;
		var controller = Controller();
		if (controller != null && !controller.customCameraPosEnabled) controller.customCameraPosEnabled = true;
		UpdateLook();
		camera.transform.SetPositionAndRotation(head, frame * Quaternion.Euler(lookPitch, lookYaw, 0f));
		CameraTarget = head;
		CameraPlaced = true;
		CameraFrames++;
	}

	private static void UpdateLook()
	{
		if (!lookAxes || Time.timeScale <= 0f) return;
		try
		{
			lookYaw = Mathf.Clamp(lookYaw + Input.GetAxis("Mouse X") * LookDegreesPerUnit, -MaxLookYaw, MaxLookYaw);
			lookPitch = Mathf.Clamp(lookPitch - Input.GetAxis("Mouse Y") * LookDegreesPerUnit, -MaxLookPitch, MaxLookPitch);
		}
		catch (Exception e)
		{
			lookAxes = false;
			Log.Warn($"[Ride] No mouse look: {e.Message.Split('\n')[0]}");
		}
	}

	private static void MeasureDrift()
	{
		if (!CameraPlaced) return;
		var camera = Camera.main;
		if (camera == null) return;
		CameraDrift = Vector3.Distance(camera.transform.position, CameraTarget);
		if (CameraDrift > MaxCameraDrift) MaxCameraDrift = CameraDrift;
	}

	private static void ReleaseCamera()
	{
		if (!CameraPlaced) return;
		CameraPlaced = false;
		var controller = Controller();
		if (controller != null) controller.customCameraPosEnabled = false;
	}

	private static void PlaceAvatars()
	{
		bool retry = Time.realtimeSinceStartup >= nextAvatarRetry;
		if (retry) nextAvatarRetry = Time.realtimeSinceStartup + AvatarRetryInterval;
		foreach (int playerId in rides.Values.SelectMany(r => new[] { r.PassengerId, r.DriverId }).Distinct())
		{
			if (!PresenceManager.Roster.TryGetValue(playerId, out var player)) continue;
			if (!TryGetAvatarSeat(playerId, out var position, out var rotation)) continue;
			if (!player.HasAvatar)
			{
				if (retry) PresenceManager.Reconcile(playerId);
				continue;
			}
			var avatar = player.Avatar;
			if (!avatar.gameObject.activeSelf) avatar.gameObject.SetActive(true);
			avatar.UpdateNetworkState(position, rotation, Vector3.zero, 0f, true, true, false);
			avatar.transform.SetPositionAndRotation(position, rotation);
		}
	}
}
