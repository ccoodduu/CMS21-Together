using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;
using Newtonsoft.Json;

namespace CMS21_Together_Server.Data.Cars
{
	// sync-car-details D7/D8: stores the latest details per loader (sections replace, keyed entries merge), clamps
	// them, relays to every synced client including the sender, and asks a client for a full snapshot when a loaded car
	// has none. Callers hold StateLock.
	public static class CarDetailsStore
	{
		private const float MissingGraceSeconds = 10f;
		private const int MaxFluids = 16;
		private const int MaxGears = 12;
		private const int MaxModules = 16;
		private const int MaxTuningValues = 64;
		private const int MaxCosmetics = 256;
		private const int MaxString = 64;
		private const float MaxTestDriveKm = 2000f;

		private static CarState State => GameDataManager.CurrentState.CarState;
		private static readonly Dictionary<int, float> missingSince = new Dictionary<int, float>();
		private static readonly Dictionary<int, float> nextRequest = new Dictionary<int, float>();
		private static int requestCursor;

		public static bool IsValid(int loader, out ModCarDetails details)
		{
			details = null;
			return State.LoadedCars.TryGetValue(loader, out var car) && State.Details.TryGetValue(loader, out details)
			       && details.HasSnapshot && details.SpawnSeq == car.SpawnSeq;
		}

		public static void OnUpdate(int clientId, CarDetailsUpdatePacket packet)
		{
			if (!State.LoadedCars.TryGetValue(packet.CarLoaderID, out var car) || car.SpawnSeq != packet.SpawnSeq || packet.Details == null)
			{
				Logger.Debug($"[CarDetails] Update for loader {packet.CarLoaderID} (SpawnSeq {packet.SpawnSeq}) from client {clientId} dropped.");
				return;
			}
			if (CarAwayRegistry.Blocks(packet.CarLoaderID, clientId, "details update"))
			{
				if (IsValid(packet.CarLoaderID, out var current))
					Server.SendToClient(new CarDetailsUpdatePacket { CarLoaderID = packet.CarLoaderID, SpawnSeq = current.SpawnSeq, IsFull = true, SourceClientId = -1, Details = current }, clientId);
				return;
			}
			var incoming = Clamp(packet.Details);
			incoming.SpawnSeq = packet.SpawnSeq;
			if (packet.IsFull || !State.Details.TryGetValue(packet.CarLoaderID, out var stored) || stored.SpawnSeq != packet.SpawnSeq)
			{
				incoming.HasSnapshot = packet.IsFull;
				State.Details[packet.CarLoaderID] = incoming;
				if (packet.IsFull) Logger.Info($"[CarDetails] Loader {packet.CarLoaderID}: full snapshot from client {clientId}.");
			}
			else
			{
				DetailsMerge.Merge(stored, incoming, packet.WheelMask, packet.AlignmentMask);
			}
			missingSince.Remove(packet.CarLoaderID);
			if (!packet.IsFull)
				Logger.Debug($"[CarDetails] Loader {packet.CarLoaderID}: update {packet.ClientSeq} from client {clientId} ({string.Join(", ", DetailsMerge.CarriedSignatures(incoming, packet.WheelMask, packet.AlignmentMask).Keys.Take(8))}).");
			packet.Details = incoming;
			packet.SourceClientId = clientId;
			Server.SendToClients(packet);
		}

		private static ModCarDetails Clamp(ModCarDetails details)
		{
			if (details.Fluids != null)
			{
				details.Fluids = details.Fluids.Take(MaxFluids).ToList();
				foreach (var fluid in details.Fluids)
				{
					fluid.Level = Clamp01(fluid.Level);
					fluid.Condition = Clamp01(fluid.Condition);
				}
			}
			if (details.BodyCosmetics != null)
			{
				details.BodyCosmetics = details.BodyCosmetics.Take(MaxCosmetics).ToList();
				foreach (var part in details.BodyCosmetics)
				{
					part.Dust = Clamp01(part.Dust);
					part.WashFactor = Clamp01(part.WashFactor);
					part.Livery = Cut(part.Livery);
				}
			}
			if (details.Tuning != null)
			{
				if (details.Tuning.Gearbox?.GearRatio != null && details.Tuning.Gearbox.GearRatio.Length > MaxGears)
					details.Tuning.Gearbox.GearRatio = details.Tuning.Gearbox.GearRatio.Take(MaxGears).ToArray();
				details.Tuning.Modules = (details.Tuning.Modules ?? new List<ModPartTuning>()).Take(MaxModules).ToList();
				foreach (var module in details.Tuning.Modules)
					if (module.Data?.Values != null && module.Data.Values.Length > MaxTuningValues)
						module.Data.Values = module.Data.Values.Take(MaxTuningValues).ToArray();
			}
			if (details.Plates != null)
			{
				details.Plates.LicensePlateNumberFront = Cut(details.Plates.LicensePlateNumberFront);
				details.Plates.LicensePlateNumberRear = Cut(details.Plates.LicensePlateNumberRear);
				details.Plates.FactoryLicensePlateNumber = Cut(details.Plates.FactoryLicensePlateNumber);
				details.Plates.LicensePlateFrontTex = Cut(details.Plates.LicensePlateFrontTex);
				details.Plates.LicensePlateRearTex = Cut(details.Plates.LicensePlateRearTex);
			}
			if (details.BonusParts?.IDs != null) details.BonusParts.IDs = details.BonusParts.IDs.Take(MaxCosmetics).Select(Cut).ToArray();
			if (details.Dyno != null) ClampDyno(details.Dyno);
			return details;
		}

		private static void ClampDyno(ModDynoResult dyno)
		{
			dyno.MeasuredDragIndex = Math.Max(0, dyno.MeasuredDragIndex);
			var engine = dyno.Engine;
			if (engine == null) return;
			engine.IdleRpm = Finite(engine.IdleRpm);
			engine.IdleRpmTorque = Finite(engine.IdleRpmTorque);
			engine.IdleRpmCurveBias = Finite(engine.IdleRpmCurveBias);
			engine.PeakRpm = Finite(engine.PeakRpm);
			engine.PeakRpmTorque = Finite(engine.PeakRpmTorque);
			engine.PeakRpmCurveBias = Finite(engine.PeakRpmCurveBias);
			engine.MaxRpm = Finite(engine.MaxRpm);
			engine.Inertia = Finite(engine.Inertia);
			engine.EngineFrictionTorque = Finite(engine.EngineFrictionTorque);
			engine.EngineFrictionRotational = Finite(engine.EngineFrictionRotational);
			engine.EngineFrictionViscous = Finite(engine.EngineFrictionViscous);
			engine.LimiterTriggerRpm = Finite(engine.LimiterTriggerRpm);
			engine.TuningValue = Finite(engine.TuningValue);
		}

		private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

		private static float Clamp01(float value) => float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));

		private static string Cut(string value) => value == null || value.Length <= MaxString ? value : value.Substring(0, MaxString);

		public static void Tick(float now)
		{
			foreach (int loader in State.Details.Keys.ToList())
				if (!State.LoadedCars.TryGetValue(loader, out var car) || car.SpawnSeq != State.Details[loader].SpawnSeq)
					State.Details.Remove(loader);

			var clients = Server.Clients.Values.Where(c => c.IsConnected && c.SyncState == SyncState.InSession).Select(c => c.ID).OrderBy(id => id).ToList();
			foreach (var pair in State.LoadedCars)
			{
				if (!pair.Value.HasBaseline || IsValid(pair.Key, out _))
				{
					missingSince.Remove(pair.Key);
					continue;
				}
				if (!missingSince.TryGetValue(pair.Key, out float since))
				{
					missingSince[pair.Key] = now;
					continue;
				}
				if (now - since < MissingGraceSeconds || clients.Count == 0) continue;
				if (nextRequest.TryGetValue(pair.Key, out float next) && now < next) continue;
				int client = clients[requestCursor++ % clients.Count];
				nextRequest[pair.Key] = now + MissingGraceSeconds;
				Logger.Info($"[CarDetails] Loader {pair.Key} has no details snapshot; asking client {client}.");
				Server.SendToClient(new CarDetailsRequestPacket { CarLoaderID = pair.Key, SpawnSeq = pair.Value.SpawnSeq }, client);
			}
		}

		public static bool FoldTestDrive(int clientId, TestDriveResultPacket packet)
		{
			if (!CarAwayRegistry.IsOwner(packet.CarLoaderID, clientId, CarAwayKind.TestTrack, packet.SpawnSeq) || !IsValid(packet.CarLoaderID, out var stored))
			{
				Logger.Info($"[CarDetails] Test drive result for loader {packet.CarLoaderID} from client {clientId} not applied (no claim or no valid details).");
				return false;
			}
			float delta = Math.Max(0f, Math.Min(MaxTestDriveKm, Finite(packet.MileageDeltaKm)));
			var fold = new ModCarDetails { SpawnSeq = stored.SpawnSeq };
			if (stored.Info != null)
			{
				stored.Info.Mileage += (int)Math.Round(delta);
				fold.Info = stored.Info;
			}
			if (packet.Cosmetics != null && stored.BodyCosmetics != null)
			{
				fold.BodyCosmetics = new List<ModBodyCosmetics>();
				foreach (var part in packet.Cosmetics.Take(MaxCosmetics))
				{
					var target = stored.BodyCosmetics.FirstOrDefault(p => p.PartIndex == part.PartIndex);
					if (target == null) continue;
					target.Dust = Clamp01(part.Dust);
					target.WashFactor = Clamp01(part.WashFactor);
					fold.BodyCosmetics.Add(target);
				}
			}
			Logger.Info($"[CarDetails] Loader {packet.CarLoaderID}: test drive by client {clientId} folded (+{delta:0.#} km, {fold.BodyCosmetics?.Count ?? 0} parts).");
			Server.SendToClients(new CarDetailsUpdatePacket { CarLoaderID = packet.CarLoaderID, SpawnSeq = stored.SpawnSeq, SourceClientId = -1, Details = fold });
			return true;
		}

		public static void SendTo(int loader, int clientId)
		{
			if (IsValid(loader, out var details))
				Server.SendToClient(new CarDetailsUpdatePacket { CarLoaderID = loader, SpawnSeq = details.SpawnSeq, IsFull = true, SourceClientId = -1, Details = details }, clientId);
		}

		public static int SendSnapshot(int clientId)
		{
			int sent = 0;
			foreach (var pair in State.Details.ToList())
			{
				if (!IsValid(pair.Key, out var details)) continue;
				Server.SendToClient(new CarDetailsUpdatePacket { CarLoaderID = pair.Key, SpawnSeq = details.SpawnSeq, IsFull = true, SourceClientId = -1, Details = details }, clientId);
				sent++;
			}
			return sent;
		}

		public static string Describe(int loader) =>
			!State.LoadedCars.ContainsKey(loader) ? "no car"
			: State.Details.TryGetValue(loader, out var details) ? JsonConvert.SerializeObject(details) : "no details";
	}
}
