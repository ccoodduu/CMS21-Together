using System.Collections.Generic;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Garage
{
	// shared-garage-look D1/D2: the stored look and the one-editor claim. Callers hold StateLock.
	public static class GarageLookService
	{
		public const int NoHolder = GarageLookClaimResultPacket.NoHolder;

		public static int Holder { get; private set; } = NoHolder;

		public static ModGarageLook Look
		{
			get
			{
				var garage = GameDataManager.CurrentState.GarageState;
				return garage.Look ?? (garage.Look = new ModGarageLook());
			}
		}

		public static void Initialize()
		{
			PresenceEvents.Left += clientId => Release(clientId, "left the session");
			PresenceEvents.SceneChanged += (clientId, from, to) =>
			{
				if (from == GameScene.Garage && to != GameScene.Garage) Release(clientId, "left the garage");
			};
		}

		public static void ResetRuntime() => Holder = NoHolder;

		public static bool Claim(int clientId)
		{
			if (Holder != NoHolder && Holder != clientId)
			{
				Logger.Info($"[GarageLook] claim by client {clientId} refused, held by {Holder}.");
				return false;
			}
			Holder = clientId;
			Logger.Info($"[GarageLook] claimed by client {clientId}.");
			return true;
		}

		public static void OnClaim(int clientId, GarageLookClaimPacket packet)
		{
			if (packet.Release)
			{
				Release(clientId, "window closed");
				return;
			}
			bool granted = Claim(clientId);
			Server.SendToClient(new GarageLookClaimResultPacket { Granted = granted, HolderPlayerId = Holder }, clientId);
		}

		public static void Release(int clientId, string why)
		{
			if (Holder != clientId) return;
			Holder = NoHolder;
			Logger.Info($"[GarageLook] claim of client {clientId} released ({why}).");
		}

		public static bool Store(int clientId, ModGarageLook incoming)
		{
			if (incoming == null) return false;
			if (Holder != clientId)
			{
				Logger.Info($"[GarageLook] update from client {clientId} refused: the claim is held by {(Holder == NoHolder ? "nobody" : Holder.ToString())}.");
				return false;
			}
			var look = incoming.Clamped();
			GameDataManager.CurrentState.GarageState.Look = look;
			Logger.Info($"[GarageLook] client {clientId} stored the look: {look.Describe()}.");
			return true;
		}

		public static void OnUpdate(int clientId, GarageLookUpdatePacket packet)
		{
			if (Store(clientId, packet.Look))
				Server.SendToClients(new GarageLookUpdatePacket { Look = Look });
			else
				Server.SendToClient(new GarageLookUpdatePacket { Look = Look }, clientId);
		}

		public static IEnumerable<string> Describe()
		{
			yield return $"look: {Look.Describe()}";
			yield return $"claim: {(Holder == NoHolder ? "free" : $"held by client {Holder}")}";
		}
	}
}
