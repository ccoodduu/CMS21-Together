using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car;
using CMS21Together.Logic.Player;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CMS21Together.Logic.Driving;

public class RemoteCar
{
	public int PlayerId;
	public CarDriveStartPacket Start;
	public GameObject Holder;
	public CarLoader Loader;
	public Transform Root;
	public string Mode = "loading";
	public string Route;
	public readonly DriveInterpolator Interpolator = new DriveInterpolator();
	public readonly List<Transform> Wheels = new List<Transform>();
	public readonly List<Quaternion> WheelRest = new List<Quaternion>();
	public readonly List<bool> WheelSteers = new List<bool>();
	public float WheelAngle;
	public RemoteEngine Engine;
	public float BuildSeconds;
	public long BuildBytes;
	public bool Kinematic;
	public bool CollidersOff;
	public int DisabledScripts;
	public bool Stopped;

	public int DriveId => Start.DriveId;
	public bool Ready => Root != null && Root;
}

// remote-visual-feedback D10: an observer shows another player's track car as an inert copy (no physics, no
// colliders, no part scripts) on a loader of its own, moved from the driver's stream.
public static class RemoteCars
{
	public const float LoadTimeoutSeconds = 30f;
	private static readonly Vector3 ParkingSpot = new Vector3(0f, -500f, 0f);

	private static readonly Dictionary<int, RemoteCar> cars = new Dictionary<int, RemoteCar>();
	private static bool subscribed;

	public static IEnumerable<RemoteCar> All => cars.Values;
	public static int StatesReceived { get; private set; }
	public static int StatesIgnored { get; private set; }

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		ClientScene.LeavingScene += (from, to) => Clear($"left {from}");
		PresenceManager.PlayerRemoved += (record, reason) => Remove(record.PlayerId, "player left");
	}

	public static void Reset() => Clear("reset");

	public static void OnStart(CarDriveStartPacket packet)
	{
		if (packet == null || packet.Scene != ClientScene.LocalScene) return;
		Log.Info($"[Drive] Player {packet.PlayerId} drives {packet.CarToLoad} (drive {packet.DriveId}); building the observer car.");
		Spawn(packet, null);
	}

	public static RemoteCar Spawn(CarDriveStartPacket packet, string route)
	{
		Remove(packet.PlayerId, "new drive");
		var car = new RemoteCar { PlayerId = packet.PlayerId, Start = packet };
		cars[packet.PlayerId] = car;
		MelonCoroutines.Start(Build(car, route));
		return car;
	}

	public static void Push(int playerId, DriveState state)
	{
		if (cars.TryGetValue(playerId, out var car) && !car.Stopped) car.Interpolator.Push(state, Time.time);
	}

	public static void OnState(CarDriveStatePacket packet)
	{
		if (packet == null || !cars.TryGetValue(packet.PlayerId, out var car) || car.DriveId != packet.DriveId || car.Stopped
		    || !DriveStateCodec.TryDecode(packet.Payload, out var state))
		{
			StatesIgnored++;
			return;
		}
		StatesReceived++;
		car.Interpolator.Push(state, Time.time);
	}

	public static void OnStop(CarDriveStopPacket packet)
	{
		if (packet == null || !cars.TryGetValue(packet.PlayerId, out var car) || car.DriveId != packet.DriveId) return;
		Remove(packet.PlayerId, "drive stopped");
	}

	public static void Remove(int playerId, string why)
	{
		if (!cars.TryGetValue(playerId, out var car)) return;
		cars.Remove(playerId);
		Destroy(car);
		Log.Info($"[Drive] Observer car of player {playerId} removed ({why}).");
	}

	private static void Clear(string why)
	{
		foreach (int id in cars.Keys.ToList()) Remove(id, why);
	}

	private static void Destroy(RemoteCar car)
	{
		car.Stopped = true;
		if (car.Engine?.Sound != null && car.Engine.Sound) Object.Destroy(car.Engine.Sound);
		car.Engine = null;
		if (car.Holder != null && car.Holder) Object.Destroy(car.Holder);
		car.Holder = null;
		car.Root = null;
	}

	private static IEnumerator Build(RemoteCar car, string forceRoute)
	{
		float started = Time.realtimeSinceStartup;
		long memoryBefore = GC.GetTotalMemory(false) + ProcessBytes();
		NewCarData data = null;
		if (forceRoute != "base" && car.Start.CarBlob != null && car.Start.CarBlobVersion == NewCarDataCodec.SaveVersion)
		{
			try { data = NewCarDataCodec.Deserialize(car.Start.CarBlob, car.Start.CarBlobVersion); }
			catch (Exception e) { Log.Warn($"[Drive] Car data of player {car.PlayerId} unreadable ({e.Message.Split('\n')[0]}); using the base model."); }
		}

		string route = forceRoute == "new" ? "new" : "clone";
		if (!CreateLoader(car, route)) yield break;
		car.Route = route;
		car.Mode = data != null ? "ghost" : "ghost=base";
		try
		{
			car.Loader.StartCoroutine(data != null ? car.Loader.LoadCarFromFile(data) : car.Loader.LoadCar(car.Start.CarToLoad));
		}
		catch (Exception e)
		{
			Fail(car, $"load threw {e.GetType().Name}: {e.Message.Split('\n')[0]}");
			yield break;
		}

		float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
		while (!car.Stopped && car.Loader != null && car.Loader && !car.Loader.IsCarLoaded() && Time.realtimeSinceStartup < deadline) yield return null;
		if (car.Stopped) yield break;
		if (car.Loader == null || !car.Loader || !car.Loader.IsCarLoaded())
		{
			Fail(car, "the car did not load");
			yield break;
		}
		yield return null;
		if (car.Stopped) yield break;

		try
		{
			MakeInert(car);
			FindWheels(car);
			car.Engine = RemoteEngines.Create(car.Loader, car.PlayerId, -1);
			if (car.Engine?.Sound != null) car.Engine.Sound.name = $"RemoteDriveEngine[{car.PlayerId}]";
		}
		catch (Exception e)
		{
			Log.Warn($"[Drive] Observer car of player {car.PlayerId}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
		}
		car.BuildSeconds = Time.realtimeSinceStartup - started;
		car.BuildBytes = GC.GetTotalMemory(false) + ProcessBytes() - memoryBefore;
		car.Root.gameObject.SetActive(car.Interpolator.HasState);
		Log.Info($"[Drive] Observer car of player {car.PlayerId} ready: {car.Mode} by {car.Route} in {car.BuildSeconds:F1} s, {car.BuildBytes / 1048576f:F0} MB, {car.Wheels.Count} wheels, engine {(car.Engine != null ? "on" : "silent")}.");
	}

	private static long ProcessBytes()
	{
		try { return System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64; }
		catch (Exception) { return 0; }
	}

	private static bool CreateLoader(RemoteCar car, string route)
	{
		try
		{
			if (route == "clone")
			{
				var physics = PrepareCarPhysics.Get();
				var template = physics != null && physics.CarLoader != null ? physics.CarLoader : Object.FindObjectOfType<CarLoader>();
				if (template == null) throw new InvalidOperationException("no track CarLoader");
				var inactive = new GameObject("RemoteCarStaging");
				inactive.SetActive(false);
				car.Holder = Object.Instantiate(template.gameObject, inactive.transform);
				for (int i = car.Holder.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(car.Holder.transform.GetChild(i).gameObject);
				foreach (var behaviour in car.Holder.GetComponents<MonoBehaviour>())
					if (behaviour != null && behaviour.GetIl2CppType().Name != nameof(CarLoader)) Object.DestroyImmediate(behaviour);
				car.Loader = car.Holder.GetComponent<CarLoader>();
				car.Loader.loadOnStart = false;
				car.Holder.transform.SetParent(null, false);
				Object.Destroy(inactive);
			}
			else
			{
				car.Holder = new GameObject();
				car.Loader = car.Holder.AddComponent<CarLoader>();
			}
			car.Holder.name = $"RemoteCar[{car.PlayerId}]";
			car.Holder.transform.SetPositionAndRotation(ParkingSpot, Quaternion.identity);
			car.Loader.loadOnStart = false;
			car.Loader.addInteractiveObject = false;
			car.Holder.SetActive(true);
			return true;
		}
		catch (Exception e)
		{
			Fail(car, $"{route}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
			return false;
		}
	}

	private static void Fail(RemoteCar car, string why)
	{
		car.Mode = "failed";
		Log.Warn($"[Drive] Observer car of player {car.PlayerId} not shown: {why}.");
		if (car.Holder != null && car.Holder) Object.Destroy(car.Holder);
		car.Holder = null;
		car.Root = null;
	}

	private static void MakeInert(RemoteCar car)
	{
		car.Root = car.Loader.GetRootTransform();
		if (car.Root == null || !car.Root) car.Root = car.Holder.transform;
		var bodies = car.Holder.GetComponentsInChildren<Rigidbody>(true);
		foreach (var body in bodies)
		{
			body.isKinematic = true;
			body.detectCollisions = false;
		}
		foreach (var collider in car.Holder.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
		int disabled = 0;
		foreach (var script in car.Holder.GetComponentsInChildren<PartScript>(true))
		{
			script.enabled = false;
			disabled++;
		}
		foreach (var mount in car.Holder.GetComponentsInChildren<MountObject>(true))
		{
			mount.enabled = false;
			disabled++;
		}
		foreach (var interactive in car.Holder.GetComponentsInChildren<InteractiveObject>(true))
		{
			interactive.enabled = false;
			disabled++;
		}
		car.DisabledScripts = disabled;
		car.Kinematic = bodies.All(b => b.isKinematic);
		car.CollidersOff = car.Holder.GetComponentsInChildren<Collider>(true).All(c => !c.enabled);
	}

	private static void FindWheels(RemoteCar car)
	{
		var handles = new[] { car.Loader.GetWheelFLHandle(), car.Loader.GetWheelFRHandle(), car.Loader.GetWheelRLHandle(), car.Loader.GetWheelRRHandle() };
		var rootRotation = car.Root.rotation;
		for (int i = 0; i < handles.Length; i++)
		{
			if (handles[i] == null || !handles[i]) continue;
			car.Wheels.Add(handles[i].transform);
			car.WheelRest.Add(Quaternion.Inverse(rootRotation) * handles[i].transform.rotation);
			car.WheelSteers.Add(i < 2);
		}
	}

	public static void Update()
	{
		if (cars.Count == 0) return;
		float now = Time.time;
		foreach (var car in cars.Values)
		{
			if (!car.Ready || car.Stopped) continue;
			if (!car.Interpolator.Sample(now, Time.deltaTime)) continue;
			if (!car.Root.gameObject.activeSelf) car.Root.gameObject.SetActive(true);
			car.Root.SetPositionAndRotation(car.Interpolator.Position, car.Interpolator.Rotation);

			var newest = car.Interpolator.Newest;
			car.WheelAngle = (car.WheelAngle + newest.WheelRadPerSecond * 57.29578f * Time.deltaTime) % 360f;
			var steer = Quaternion.AngleAxis(newest.SteerDegrees, Vector3.up);
			var spin = Quaternion.AngleAxis(car.WheelAngle, Vector3.right);
			for (int i = 0; i < car.Wheels.Count; i++)
			{
				if (car.Wheels[i] == null || !car.Wheels[i]) continue;
				car.Wheels[i].rotation = car.Root.rotation * (car.WheelSteers[i] ? steer : Quaternion.identity) * spin * car.WheelRest[i];
			}

			if (car.Engine != null && car.Engine.IsAlive)
			{
				car.Engine.Res.engineCurrentRPM = Mathf.Max(newest.Rpm, car.Loader.EngineData.idleRpm);
				if (car.Engine.Res.mainCamera == null || !car.Engine.Res.mainCamera) car.Engine.Res.mainCamera = Camera.main;
			}
		}
	}
}
