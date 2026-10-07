using System;
using System.Collections.Generic;
using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	// remote-visual-feedback D5: the latest activity per player lives in the presence record (never saved) and is
	// relayed like movement. The server decides nothing about game state.
	public static class VisualHandlers
	{
		public const int MaxPerSecond = 8;
		private const float WarnIntervalSeconds = 60f;

		private class Budget
		{
			public float WindowStart;
			public int Count;
			public int Dropped;
			public float LastWarn = float.MinValue;
		}

		private static readonly Dictionary<int, Budget> budgets = new Dictionary<int, Budget>();

		public static void Initialize()
		{
			PresenceEvents.SceneChanged += OnSceneChanged;
			PresenceEvents.Left += clientId => budgets.Remove(clientId);
		}

		[PacketHandler(PacketTypes.PlayerActivity)]
		[AllowBeforeSync]
		public static void OnPlayerActivity(long clientId, PlayerActivityPacket packet)
		{
			int id = (int)clientId;
			var record = PresenceRegistry.Get(id);
			if (record == null || packet?.State == null || !Enum.IsDefined(typeof(ActivityKind), packet.State.Kind)) return;
			if (!WithinBudget(id)) return;

			var state = packet.State.Clone();
			if (state.Progress > PlayerActivityState.ProgressSteps) state.Progress = PlayerActivityState.ProgressSteps;
			if (state.CarLoaderID != PlayerActivityState.NoCar && CarPartsStore.Get(state.CarLoaderID) == null)
				state.CarLoaderID = PlayerActivityState.NoCar;
			record.Activity = state.IsIdle ? null : state;
			Relay(record, new PlayerActivityPacket { PlayerId = id, State = state });
			Logger.Debug($"[Visuals] Activity of player {id}: {state}.");
		}

		private static void Relay(PlayerPresenceRecord record, PlayerActivityPacket packet)
		{
			if (!GameSceneInfo.ShowsAvatars(record.Scene)) return;
			foreach (var other in PresenceRegistry.All)
			{
				if (other.PlayerId == record.PlayerId || other.Scene != record.Scene) continue;
				if (!Server.Clients.TryGetValue(other.PlayerId, out var client)) continue;
				if (client.IsConnected && client.SyncState != SyncState.Connected) Server.SendToClient(packet, other.PlayerId);
			}
		}

		private static bool WithinBudget(int clientId)
		{
			if (!budgets.TryGetValue(clientId, out var budget))
			{
				budget = new Budget { WindowStart = ServerTime.Time };
				budgets[clientId] = budget;
			}
			float now = ServerTime.Time;
			if (now - budget.WindowStart >= 1f)
			{
				budget.WindowStart = now;
				budget.Count = 0;
			}
			if (++budget.Count <= MaxPerSecond) return true;

			budget.Dropped++;
			if (now - budget.LastWarn >= WarnIntervalSeconds)
			{
				budget.LastWarn = now;
				Logger.Warn($"[Visuals] Client {clientId} sends more than {MaxPerSecond} activity packets per second; dropped {budget.Dropped} so far.");
			}
			return false;
		}

		// Activity is relayed only within a scene, so a player entering a scene has missed the changes made there.
		private static void OnSceneChanged(int clientId, GameScene from, GameScene to)
		{
			var record = PresenceRegistry.Get(clientId);
			if (record == null) return;
			record.Activity = null;
			if (!GameSceneInfo.ShowsAvatars(to) || !Server.Clients.TryGetValue(clientId, out var client) || !client.IsConnected || client.SyncState == SyncState.Connected) return;
			foreach (var other in PresenceRegistry.All)
			{
				if (other.PlayerId == clientId || other.Scene != to) continue;
				Server.SendToClient(new PlayerActivityPacket { PlayerId = other.PlayerId, State = other.Activity?.Clone() ?? PlayerActivityState.Idle() }, clientId);
			}
		}
	}
}
