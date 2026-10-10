using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using CMS21Together.UI;
using UnityEngine;

namespace CMS21Together.Logic.Car.Away;

// sync-test-drive-and-diagnostics D1/D3: mirror of the server's away claims (tracks, test path, dyno), requests
// with a timeout, and the lock check other features call before they edit a car.
public static class CarAwaySync
{
	private const float TimeoutSeconds = 5f;

	public class Away
	{
		public int Owner;
		public CarAwayKind Kind;
		public int SpawnSeq;
	}

	private class Pending
	{
		public int Loader;
		public Action OnGranted;
		public Action<CarAwayRefusal, int> OnRefused;
		public float Deadline;
	}

	private static readonly Dictionary<int, Away> mirror = new Dictionary<int, Away>();
	private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
	private static int nextRequestId = 1;

	public static event Action<int, int> Released;

	public static string LastBlocked { get; set; }

	public static IReadOnlyDictionary<int, Away> All => mirror;

	public static void Reset()
	{
		mirror.Clear();
		pending.Clear();
		LastBlocked = null;
	}

	public static void OnUpdate(CarAwayUpdatePacket packet)
	{
		if (packet.OwnerPlayerId >= 0)
		{
			mirror[packet.CarLoaderID] = new Away { Owner = packet.OwnerPlayerId, Kind = packet.Kind, SpawnSeq = packet.SpawnSeq };
			Log.Info($"[Away] Loader {packet.CarLoaderID}: {packet.Kind} by player {packet.OwnerPlayerId}.");
		}
		else if (packet.Refusal == CarAwayRefusal.None && mirror.Remove(packet.CarLoaderID))
		{
			Log.Info($"[Away] Loader {packet.CarLoaderID}: {packet.Kind} released.");
			Released?.Invoke(packet.CarLoaderID, packet.SpecialState);
		}

		if (packet.RequestId == 0 || !pending.TryGetValue(packet.RequestId, out var request)) return;
		pending.Remove(packet.RequestId);
		if (packet.Refusal == CarAwayRefusal.None && packet.OwnerPlayerId == Client.Instance.ID) request.OnGranted?.Invoke();
		else
		{
			Log.Info($"[Away] Request {packet.RequestId} for loader {request.Loader} refused: {packet.Refusal}.");
			request.OnRefused?.Invoke(packet.Refusal, packet.Refusal == CarAwayRefusal.Seated ? packet.HolderPlayerId : packet.OwnerPlayerId);
		}
	}

	public static void Request(int loader, CarAwayKind kind, Action onGranted, Action<CarAwayRefusal, int> onRefused)
	{
		if (IsMine(loader, kind))
		{
			onGranted?.Invoke();
			return;
		}
		int requestId = nextRequestId++;
		pending[requestId] = new Pending { Loader = loader, OnGranted = onGranted, OnRefused = onRefused, Deadline = Time.realtimeSinceStartup + TimeoutSeconds };
		Log.Info($"[Away] Request {requestId}: {kind} on loader {loader}.");
		Client.Instance.Send(new CarAwayRequestPacket { RequestId = requestId, CarLoaderID = loader, SpawnSeq = CarPartsSync.SpawnSeq(loader), Kind = kind });
	}

	public static void Update()
	{
		if (pending.Count == 0) return;
		float now = Time.realtimeSinceStartup;
		foreach (var pair in pending.Where(p => now > p.Value.Deadline).ToList())
		{
			pending.Remove(pair.Key);
			Log.Info($"[Away] Request {pair.Key} for loader {pair.Value.Loader} timed out.");
			pair.Value.OnRefused?.Invoke(CarAwayRefusal.Unknown, -1);
		}
	}

	public static void Release(int loader, int specialState = -1)
	{
		if (!mirror.TryGetValue(loader, out var away) || away.Owner != Client.Instance.ID) return;
		Client.Instance.Send(new CarAwayReleasePacket { CarLoaderID = loader, SpawnSeq = away.SpawnSeq, SpecialState = specialState });
	}

	public static bool IsMine(int loader, CarAwayKind kind) =>
		mirror.TryGetValue(loader, out var away) && away.Owner == Client.Instance.ID && away.Kind == kind;

	public static bool LockedForMe(int loader, out int owner, out CarAwayKind kind)
	{
		owner = -1;
		kind = CarAwayKind.TestTrack;
		if (!mirror.TryGetValue(loader, out var away) || away.Owner == Client.Instance.ID) return false;
		owner = away.Owner;
		kind = away.Kind;
		return true;
	}

	public static bool BlockIfLocked(int loader, string what)
	{
		if (!LockedForMe(loader, out int owner, out var kind)) return false;
		LastBlocked = $"{what} on loader {loader}";
		Log.Info($"[Away] {what} on loader {loader} blocked: {kind} by player {owner}.");
		ModNotify.ShowToast($"{OwnerName(owner)} has this car {Activity(kind)}.");
		return true;
	}

	public static string OwnerName(int owner) =>
		PresenceManager.Roster.TryGetValue(owner, out var player) ? player.Record.Username : $"Player {owner}";

	public static string Activity(CarAwayKind kind) => kind switch
	{
		CarAwayKind.TestTrack => "on the test track",
		CarAwayKind.RaceTrack => "on the race track",
		CarAwayKind.SpeedTrack => "on the speed track",
		CarAwayKind.PathTest => "on the test path",
		_ => "on the dyno",
	};

	public static string Label(CarAwayKind kind) => kind switch
	{
		CarAwayKind.TestTrack => "test drive",
		CarAwayKind.RaceTrack => "race track",
		CarAwayKind.SpeedTrack => "speed track",
		CarAwayKind.PathTest => "test path",
		_ => "dyno",
	};
}
