using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;
using CMS21_Together_Server.Network.Handlers;

namespace CMS21_Together_Server.Data.Jobs
{
	// sync-orders-and-jobs D1-D12: orders Open -> Claimed -> Active -> removed, the elected order generator, expiry and
	// claim timeouts. Callers hold StateLock.
	public static class JobsService
	{
		private const float ClaimTimeoutSeconds = 60f;
		private const int MaxPayout = 1_000_000;
		private const int MaxXp = 10_000;

		private static JobsState State => GameDataManager.CurrentState.JobsState;
		private static int generator = CarLoaderEntry.NoClient;
		private static bool checkedLoadedJobs;
		private static float lastTick = -1f;

		public static int Generator => generator;

		public static void Initialize()
		{
			CarPartsStore.LoaderCleared += OnLoaderCleared;
			PresenceEvents.Left += OnClientLeft;
			PresenceEvents.SceneChanged += OnSceneChanged;
		}

		public static void Reset()
		{
			generator = CarLoaderEntry.NoClient;
			checkedLoadedJobs = false;
			foreach (var order in State.Orders) order.Status = OrderStatus.Open;
		}

		// Elections

		public static void OnInSession(int clientId) => Elect();

		private static void OnSceneChanged(int clientId, GameScene from, GameScene to)
		{
			if (to != GameScene.Garage) ReleaseClaims(clientId, "left the garage");
			Elect();
		}

		public static void OnClientLeft(int clientId)
		{
			ReleaseClaims(clientId, "left");
			if (generator == clientId) generator = CarLoaderEntry.NoClient;
			Elect();
		}

		private static bool Eligible(int clientId) =>
			Server.Clients.TryGetValue(clientId, out var client) && client.IsConnected && client.SyncState == SyncState.InSession
			&& PresenceRegistry.Get(clientId)?.Scene == GameScene.Garage;

		public static void Elect()
		{
			if (generator != CarLoaderEntry.NoClient && Eligible(generator)) return;
			int previous = generator;
			generator = Server.Clients.Keys.Where(Eligible).DefaultIfEmpty(CarLoaderEntry.NoClient).Min();
			if (generator == previous) return;
			Logger.Info($"[Jobs] Order generator: {(generator == CarLoaderEntry.NoClient ? "nobody" : $"client {generator}")}.");
			if (previous != CarLoaderEntry.NoClient && Server.Clients.ContainsKey(previous))
				Server.SendToClient(new OrderGeneratorRolePacket { IsGenerator = false }, previous);
			if (generator != CarLoaderEntry.NoClient)
				Server.SendToClient(new OrderGeneratorRolePacket { IsGenerator = true }, generator);
		}

		// Orders

		public static void OnOrderGenerated(int clientId, OrderGeneratedPacket packet)
		{
			if (clientId != generator)
			{
				Logger.Info($"[Jobs] Order from client {clientId} dropped: client {generator} is the generator.");
				return;
			}
			var job = packet.Job;
			if (job == null) return;
			if (job.IsMission && job.MissionID == 0)
			{
				Logger.Info("[Jobs] Tutorial mission refused.");
				return;
			}
			int open = State.Orders.Count;
			if (packet.MaxOpenOrders > 0 && open >= packet.MaxOpenOrders)
			{
				Logger.Info($"[Jobs] Order refused: {open} open orders, the generator's limit is {packet.MaxOpenOrders}.");
				return;
			}
			job.id = State.NextJobId++;
			var entry = new OrderEntry { Job = job, RemainingSeconds = job.timeToEnd, Status = OrderStatus.Open };
			State.Orders.Add(entry);
			Logger.Info($"[Jobs] Order {job.id}: {job.carFile}{(job.IsMission ? $" (mission {job.MissionID})" : "")}, {entry.RemainingSeconds:0} s.");
			Server.SendToClients(new OrderAddedPacket { Job = job, RemainingSeconds = entry.RemainingSeconds });
		}

		public static void OnOrderAction(int clientId, OrderActionPacket packet, float now)
		{
			var order = State.Orders.FirstOrDefault(o => o.Job.id == packet.JobId);
			switch (packet.Action)
			{
				case OrderActionType.Accept:
					string refusal = order == null ? "Unknown"
						: order.Status != OrderStatus.Open ? "AlreadyTaken"
						: State.Orders.Any(o => o.Status == OrderStatus.Claimed) ? "Busy"
						: null;
					if (refusal != null)
					{
						Logger.Info($"[Jobs] Accept of order {packet.JobId} by client {clientId} refused: {refusal}.");
						Server.SendToClient(new OrderActionResultPacket { JobId = packet.JobId, Action = packet.Action, Approved = false, Reason = refusal }, clientId);
						return;
					}
					order.Status = OrderStatus.Claimed;
					order.ClaimedBy = clientId;
					order.ClaimedAt = now;
					Logger.Info($"[Jobs] Order {order.Job.id} claimed by client {clientId}.");
					Server.SendToClient(new OrderActionResultPacket { JobId = packet.JobId, Action = packet.Action, Approved = true }, clientId);
					Server.SendToClients(new JobRemovedPacket { JobId = order.Job.id, Reason = JobRemovedReason.Taken }, clientId);
					break;
				case OrderActionType.Decline:
					if (order == null || order.Status != OrderStatus.Open || !order.Job.CanDelete)
					{
						Server.SendToClient(new OrderActionResultPacket { JobId = packet.JobId, Action = packet.Action, Approved = false, Reason = order == null ? "Unknown" : "CannotDecline" }, clientId);
						return;
					}
					State.Orders.Remove(order);
					Logger.Info($"[Jobs] Order {order.Job.id} declined by client {clientId}.");
					Server.SendToClients(new JobRemovedPacket { JobId = order.Job.id, Reason = JobRemovedReason.Declined });
					break;
				case OrderActionType.AbortTake:
					if (order != null && order.Status == OrderStatus.Claimed && order.ClaimedBy == clientId) Reopen(order, "take aborted");
					break;
			}
		}

		private static void Reopen(OrderEntry order, string why)
		{
			order.Status = OrderStatus.Open;
			order.ClaimedBy = 0;
			Logger.Info($"[Jobs] Order {order.Job.id} open again ({why}).");
			Server.SendToClients(new OrderAddedPacket { Job = order.Job, RemainingSeconds = order.RemainingSeconds });
		}

		private static void ReleaseClaims(int clientId, string why)
		{
			foreach (var order in State.Orders.Where(o => o.Status == OrderStatus.Claimed && o.ClaimedBy == clientId).ToList())
			{
				DeleteJobCar(order.Job.id);
				Reopen(order, $"client {clientId} {why}");
			}
		}

		private static void DeleteJobCar(int jobId)
		{
			foreach (var pair in GameDataManager.CurrentState.CarState.LoadedCars.Where(c => c.Value.Spawn?.IsJob == true && c.Value.Spawn.JobID == jobId).ToList())
			{
				CarPartsStore.ClearLoader(pair.Key, ClearReason.Deleted);
				Server.SendToClients(new CarSpawnDeletePacket { CarLoaderID = pair.Key });
			}
		}

		public static bool IsClaimedBy(int jobId, int clientId) =>
			State.Orders.Any(o => o.Job.id == jobId && o.Status == OrderStatus.Claimed && o.ClaimedBy == clientId);

		// Active jobs

		public static void OnJobStarted(int clientId, JobStartedPacket packet)
		{
			var order = State.Orders.FirstOrDefault(o => o.Job.id == packet.JobId && o.Status == OrderStatus.Claimed && o.ClaimedBy == clientId);
			if (order == null)
			{
				Logger.Info($"[Jobs] Late start of job {packet.JobId} from client {clientId}: not claimed any more.");
				Server.SendToClient(new JobRemovedPacket { JobId = packet.JobId, Reason = JobRemovedReason.TakeAborted, CarLoaderId = packet.CarLoaderId }, clientId);
				return;
			}
			State.Orders.Remove(order);
			packet.Job.id = packet.JobId;
			State.ActiveJobs.Add(new ActiveJobEntry { Job = packet.Job, CarLoaderId = packet.CarLoaderId, OriginalSeconds = order.RemainingSeconds });
			if (packet.Missions != null) State.Missions = packet.Missions;
			Logger.Info($"[Jobs] Job {packet.JobId} started by client {clientId} on loader {packet.CarLoaderId}.");
			Server.SendToClients(packet, clientId);
		}

		public static void OnJobEnd(int clientId, JobEndRequestPacket packet)
		{
			var active = State.ActiveJobs.FirstOrDefault(j => j.Job.id == packet.JobId);
			var world = GameDataManager.CurrentState.WorldState;
			if (active == null || packet.Payout < 0 || packet.Payout > MaxPayout || packet.Xp < 0 || packet.Xp >= MaxXp)
			{
				Logger.Info($"[Jobs] End of job {packet.JobId} from client {clientId} ignored ({(active == null ? "not active" : "out of bounds")}).");
				Server.SendToClient(world, clientId);
				return;
			}
			State.ActiveJobs.Remove(active);
			world.Money += packet.Payout;
			StatsHandlers.ApplyExp(packet.Xp);
			if (packet.IsMission && packet.Missions != null) State.Missions = packet.Missions;
			int loader = active.CarLoaderId;
			if (loader >= 0 && GameDataManager.CurrentState.CarState.LoadedCars.TryGetValue(loader, out var car) && car.Spawn?.JobID == packet.JobId)
				CarPartsStore.ClearLoader(loader, ClearReason.JobEnded);
			Logger.Info($"[Jobs] Job {packet.JobId} ended by client {clientId}: payout {packet.Payout}, xp {packet.Xp}, completed {packet.IsCompleted}.");
			world.updateGamemode = false;
			Server.SendToClients(new JobRemovedPacket { JobId = packet.JobId, Reason = JobRemovedReason.Ended, CarLoaderId = loader, IsCompleted = packet.IsCompleted, Missions = State.Missions });
			Server.SendToClients(world);
		}

		private static void OnLoaderCleared(int loader, CarLoaderEntry removed, ClearReason reason)
		{
			if (reason != ClearReason.SpawnerLeft || removed.Spawn?.IsJob != true) return;
			var active = State.ActiveJobs.FirstOrDefault(j => j.CarLoaderId == loader && j.Job.id == removed.Spawn.JobID);
			if (active != null) ReopenActive(active, "its car was lost");
		}

		private static void ReopenActive(ActiveJobEntry active, string why)
		{
			State.ActiveJobs.Remove(active);
			var order = new OrderEntry { Job = active.Job, RemainingSeconds = active.OriginalSeconds, Status = OrderStatus.Open };
			State.Orders.Add(order);
			Logger.Info($"[Jobs] Job {active.Job.id} back to the open orders ({why}).");
			Server.SendToClients(new OrderAddedPacket { Job = order.Job, RemainingSeconds = order.RemainingSeconds });
		}

		// Tick: expiry, claim timeout, jobs without their car after a load

		public static void Tick(float now)
		{
			float delta = lastTick < 0f ? 0f : now - lastTick;
			lastTick = now;
			if (!checkedLoadedJobs)
			{
				checkedLoadedJobs = true;
				foreach (var active in State.ActiveJobs.ToList())
				{
					var cars = GameDataManager.CurrentState.CarState.LoadedCars;
					if (active.CarLoaderId < 0 || !cars.TryGetValue(active.CarLoaderId, out var car) || car.Spawn?.JobID != active.Job.id)
						ReopenActive(active, "its car was not in the save");
				}
			}
			if (!Server.Clients.Values.Any(c => c.IsConnected)) return;

			foreach (var order in State.Orders.ToList())
			{
				if (order.Status == OrderStatus.Claimed)
				{
					if (now - order.ClaimedAt > ClaimTimeoutSeconds)
					{
						DeleteJobCar(order.Job.id);
						Reopen(order, "claim timed out");
					}
					continue;
				}
				if (order.Job.IsMission) continue;
				order.RemainingSeconds -= delta;
				if (order.RemainingSeconds > 0f) continue;
				State.Orders.Remove(order);
				Logger.Info($"[Jobs] Order {order.Job.id} expired.");
				Server.SendToClients(new JobRemovedPacket { JobId = order.Job.id, Reason = JobRemovedReason.Expired });
			}
		}

		public static IEnumerable<string> Describe()
		{
			yield return $"generator: {(generator == CarLoaderEntry.NoClient ? "nobody" : $"client {generator}")}, next id {State.NextJobId}, missions finished {State.Missions.MissionsFinished}";
			foreach (var order in State.Orders)
				yield return $"  order {order.Job.id}: {order.Job.carFile} {order.Status}{(order.Status == OrderStatus.Claimed ? $" by {order.ClaimedBy}" : "")}, {order.RemainingSeconds:0} s{(order.Job.IsMission ? ", mission" : "")}";
			foreach (var active in State.ActiveJobs)
				yield return $"  job {active.Job.id}: {active.Job.carFile} on loader {active.CarLoaderId}";
		}

		public static int SendSnapshot(int clientId)
		{
			Server.SendToClient(new JobsStatePacket { State = State, IsGenerator = clientId == generator }, clientId);
			return 1;
		}
	}
}
