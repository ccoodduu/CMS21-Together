using System;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car;
using CMS21Together.Logic.Car.Away;
using CMS21Together.Network;
using CMS.Tracks.CarPhysics;
using UnityEngine;
using VehiclePhysics;

namespace CMS21Together.Logic.Driving;

// remote-visual-feedback D9: the driver's client streams its track car at 15 Hz. The drive starts once the track has
// set GameMode.CarDrive and loaded the car, and ends when the player leaves the track.
public static class DriveCapture
{
	public const float SendInterval = 1f / 15f;
	public const int MaxPerSecond = 15;

	private static bool subscribed;
	private static bool active;
	private static int driveId = Environment.TickCount & 0xFFFF;
	private static int seq;
	private static float nextSend;
	private static float windowStart;
	private static int windowCount;
	private static PrepareCarPhysics physics;

	public static int Sent { get; private set; }
	public static int Dropped { get; private set; }
	public static int Drives { get; private set; }
	public static bool Active => active;
	public static int DriveId => active ? driveId : 0;
	public static DriveState Last { get; private set; }

	private static bool Connected => Client.Instance != null && Client.Instance.IsConnectionValid && ClientData.IsInitialSyncFinished;

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		ClientScene.LeavingScene += (from, to) =>
		{
			if (active) Stop($"leaving {from}");
		};
	}

	public static void Reset()
	{
		active = false;
		physics = null;
	}

	public static void Update()
	{
		if (!Connected)
		{
			if (active) Reset();
			return;
		}
		if (!active)
		{
			if (ClientScene.LocalScene == GameScene.TestTrack && GameMode.Get()?.GetCurrentMode() == gameMode.CarDrive && TryFindCar(out var found))
				Start(found);
			return;
		}
		if (physics == null || !physics || ClientScene.LocalScene != GameScene.TestTrack)
		{
			Stop("track car gone");
			return;
		}
		if (Time.unscaledTime < nextSend) return;
		nextSend = Time.unscaledTime + SendInterval;
		if (!TryRead(physics, out var state)) return;
		if (!WithinBudget())
		{
			Dropped++;
			return;
		}
		Last = state;
		Client.Instance.Send(new CarDriveStatePacket { DriveId = driveId, Seq = ++seq, Payload = DriveStateCodec.Encode(state) }, false);
		Sent++;
	}

	private static bool TryFindCar(out PrepareCarPhysics found)
	{
		found = PrepareCarPhysics.Get();
		var carLoader = found == null ? null : found.CarLoader;
		return carLoader != null && carLoader.IsCarLoaded() && found.rigidBody != null;
	}

	private static void Start(PrepareCarPhysics found)
	{
		physics = found;
		active = true;
		driveId = (driveId + 1) & 0x7FFFFFFF;
		seq = 0;
		nextSend = 0f;
		Drives++;

		int loader = -1;
		foreach (var pair in CarAwaySync.All)
			if (pair.Value.Owner == Client.Instance.ID && pair.Value.Kind == CarAwayKind.TestTrack) loader = pair.Key;

		var carLoader = found.CarLoader;
		byte[] blob = null;
		try
		{
			int slot = Helper.GetIndexFromCarLoaderName(GlobalData.SelectedCarLoader);
			var data = Singleton<GameManager>.Instance.GameDataManager.LoadCarInGarage(slot);
			if (data != null && !data.IsDefault()) blob = NewCarDataCodec.Serialize(data);
		}
		catch (Exception e)
		{
			Log.Warn($"[Drive] No car data for the drive ({e.Message.Split('\n')[0]}); others see the base model.");
		}

		Client.Instance.Send(new CarDriveStartPacket
		{
			DriveId = driveId,
			Scene = ClientScene.LocalScene,
			CarLoaderID = loader,
			CarToLoad = carLoader.carToLoad,
			CarBlob = blob,
			CarBlobVersion = NewCarDataCodec.SaveVersion
		});
		Log.Info($"[Drive] Drive {driveId} of {carLoader.carToLoad} (car {loader}) started on the {ClientScene.LocalScene}, {blob?.Length ?? 0} bytes of car data.");
	}

	private static void Stop(string why)
	{
		byte[] finalPose = null;
		if (physics != null && physics && TryRead(physics, out var state)) finalPose = DriveStateCodec.Encode(state);
		Client.Instance.Send(new CarDriveStopPacket { DriveId = driveId, FinalPose = finalPose });
		Log.Info($"[Drive] Drive {driveId} stopped ({why}) after {seq} states.");
		Reset();
	}

	private static bool WithinBudget()
	{
		float now = Time.unscaledTime;
		if (now - windowStart >= 1f)
		{
			windowStart = now;
			windowCount = 0;
		}
		return ++windowCount <= MaxPerSecond;
	}

	public static Transform BodyOf(BaseCarPhysics car)
	{
		var model = car.carModel;
		if (model != null && model) return model;
		var root = car.CarLoader == null ? null : car.CarLoader.GetRootTransform();
		return root != null && root ? root : car.transform;
	}

	public static bool TryRead(BaseCarPhysics car, out DriveState state)
	{
		state = default;
		var body = BodyOf(car);
		if (body == null || !body) return false;
		var position = body.position;
		var rotation = body.rotation;
		var rigidbody = car.rigidBody;
		var velocity = rigidbody != null && rigidbody ? rigidbody.velocity : Vector3.zero;

		state.Time = Time.time;
		state.PosX = position.x;
		state.PosY = position.y;
		state.PosZ = position.z;
		state.RotX = rotation.x;
		state.RotY = rotation.y;
		state.RotZ = rotation.z;
		state.RotW = rotation.w;
		state.VelX = velocity.x;
		state.VelY = velocity.y;
		state.VelZ = velocity.z;

		var vehicle = car.VehicleController;
		if (vehicle != null && vehicle && vehicle.initialized)
		{
			var wheels = vehicle.wheelState;
			float spin = 0f;
			int count = 0;
			for (int i = 0; wheels != null && i < wheels.Length; i++)
			{
				var wheel = wheels[i];
				if (wheel == null) continue;
				if (wheel.steerable && Mathf.Abs(wheel.steerAngle) > Mathf.Abs(state.SteerDegrees)) state.SteerDegrees = wheel.steerAngle;
				spin += wheel.angularVelocity;
				count++;
			}
			if (count > 0) state.WheelRadPerSecond = spin / count;
			var data = vehicle.data;
			if (data != null)
			{
				state.Gear = data.Get(Channel.Vehicle, VehicleData.GearboxGear);
				if (data.Get(Channel.Input, InputData.Brake) > 0) state.Flags |= DriveFlags.Brake;
				if (data.Get(Channel.Vehicle, VehicleData.EngineWorking) != 0) state.Flags |= DriveFlags.EngineRunning;
				state.Rpm = data.Get(Channel.Vehicle, VehicleData.EngineRpm) / 1000f;
			}
		}
		var res = car.res;
		if (res != null && res) state.Rpm = res.engineCurrentRPM;
		if (state.Gear < 0) state.Flags |= DriveFlags.Reverse;
		if (car.CarLoader != null && car.CarLoader.LightsOn) state.Flags |= DriveFlags.Lights;
		return true;
	}
}
