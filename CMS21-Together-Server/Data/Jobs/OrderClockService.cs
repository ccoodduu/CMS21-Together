using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Jobs
{
	// docs/design/server-order-clock.md: the order timer and the open-order limit of OrderGenerator.Update, owned by the
	// server. The elected generator makes an order only when asked. Callers hold StateLock.
	public static class OrderClockService
	{
		public const float OrderInterval = 30f;
		private const float RequestTimeout = 10f;
		private const float RetryDelay = 2f;
		private const float AnswerLogInterval = 30f;

		private sealed class Request
		{
			public int Id;
			public int Client;
			public float SentAt;
		}

		private static Request pending;
		private static int lastRequestId;
		private static float retryAt;
		private static OrderRequestReason lastLoggedReason;
		private static float lastAnswerLogAt = float.NegativeInfinity;
		private static int lastLimitMismatch = -1;

		private static JobsState State => GameDataManager.CurrentState.JobsState;
		private static OrderClock Clock => State.Clock ??= new OrderClock();
		private static WorldState World => GameDataManager.CurrentState.WorldState;

		public static int Limit => MaxOrders(World?.Level ?? 0);

		public static bool Running(int generator) => generator != CarLoaderEntry.NoClient && World?.Gamemode != Gamemode.Sandbox;

		// GlobalData.GetMaxOrdersAmount (0x180D7CD50), by RealPlayerLevel (the server's WorldState.Level).
		public static int MaxOrders(int level)
		{
			if (level < 0) return 0;
			if (level <= 2) return 2;
			if (level <= 4) return 3;
			if (level <= 7) return 4;
			if (level <= 11) return 5;
			if (level <= 15) return 6;
			if (level <= 19) return 7;
			return 8;
		}

		public static void Reset()
		{
			pending = null;
			retryAt = 0f;
			lastLimitMismatch = -1;
			if (State.Clock == null) State.Clock = new OrderClock();
		}

		public static void Tick(float now, float delta, int generator)
		{
			if (pending != null && pending.Client != generator)
			{
				Logger.Info($"[Jobs] Order request {pending.Id} dropped: client {pending.Client} is no longer the generator.");
				pending = null;
			}
			if (pending != null && now - pending.SentAt > RequestTimeout)
			{
				Logger.Info($"[Jobs] Order request {pending.Id} to client {pending.Client} got no answer in {RequestTimeout:0} s.");
				pending = null;
			}
			if (!Running(generator)) return;
			int open = State.Orders.Count;
			int limit = Limit;
			if (open >= limit) return;
			var clock = Clock;
			clock.OrderTimer += delta;
			if (clock.OrderTimer <= clock.NextOrderTime || pending != null || now < retryAt) return;
			pending = new Request { Id = ++lastRequestId, Client = generator, SentAt = now };
			Logger.Info($"[Jobs] Order due: asking client {generator} (request {pending.Id}, {open} of {limit} open).");
			Server.SendToClient(new OrderRequestPacket { RequestId = pending.Id }, generator);
		}

		public static bool IsStale(int requestId) => requestId != 0 && requestId != pending?.Id;

		public static void OnAnswer(int clientId, OrderGeneratedPacket packet, float now)
		{
			if (packet.RequestId == 0 || packet.RequestId != pending?.Id)
			{
				Logger.Info($"[Jobs] Stale answer to order request {packet.RequestId} from client {clientId} ({packet.Reason}) ignored.");
				return;
			}
			pending = null;
			bool wait = packet.Reason == OrderRequestReason.NoCar || packet.Reason == OrderRequestReason.Disabled;
			if (wait) Restart();
			else retryAt = now + RetryDelay;
			if (packet.Reason == lastLoggedReason && now - lastAnswerLogAt < AnswerLogInterval) return;
			lastLoggedReason = packet.Reason;
			lastAnswerLogAt = now;
			Logger.Info($"[Jobs] Client {clientId} made no order for request {packet.RequestId}: {packet.Reason}; {(wait ? $"next try in {OrderInterval:0} s" : $"asking again in {RetryDelay:0} s")}.");
		}

		public static void CheckReportedLimit(int reported)
		{
			int limit = Limit;
			if (reported <= 0 || reported == limit || reported == lastLimitMismatch) return;
			lastLimitMismatch = reported;
			Logger.Warn($"[Jobs] The generator's order limit is {reported}, the server's is {limit} (level {World?.Level}).");
		}

		public static void OnOrderAccepted()
		{
			pending = null;
			Restart();
		}

		public static void OnRegularTaken() => Restart();

		public static void OnJobEnded() => Clock.OrderTimer = 0f;

		private static void Restart()
		{
			Clock.OrderTimer = 0f;
			Clock.NextOrderTime = OrderInterval;
		}

		public static string Describe(int generator, float now) =>
			$"clock {Clock.OrderTimer:0.0} / {Clock.NextOrderTime:0} s, {State.Orders.Count} of {Limit} open (level {World?.Level}), "
			+ (Running(generator) ? "running" : "frozen")
			+ (pending == null ? "" : $", request {pending.Id} to client {pending.Client} {now - pending.SentAt:0} s ago");
	}
}
