using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.ShopList
{
	// Callers hold StateLock.
	public static class ShopListService
	{
		private static ShopListState State => GameDataManager.CurrentState.ShopListState;

		public static void OnChange(int clientId, ShopListChangePacket change)
		{
			var entries = State.Entries;
			var refused = new List<string>();
			bool changed = false;

			foreach (var removed in change.Removed ?? new List<ShopListEntry>())
			{
				int index = removed == null ? -1 : entries.FindIndex(e => e.SameItem(removed));
				if (index < 0) refused.Add($"remove {removed?.Describe() ?? "null"}: not on the list");
				else
				{
					entries.RemoveAt(index);
					changed = true;
				}
			}

			foreach (var delta in change.Deltas ?? new List<ShopListEntry>())
			{
				string reason = Apply(entries, delta);
				if (reason == null) changed = true;
				else refused.Add($"{(delta?.Amount > 0 ? "add" : "take")} {delta?.Describe() ?? "null"}: {reason}");
			}

			if (changed) State.Revision++;
			string refusal = refused.Count > 0 ? string.Join("; ", refused) : null;
			var state = Snapshot(clientId, change.ClientSeq, refusal);
			if (changed) Server.SendToClients(state);
			else Server.SendToClient(state, clientId);

			Logger.Info($"[ShopList] Change {change.ClientSeq} from client {clientId} (-{change.Removed?.Count ?? 0} stacks, {change.Deltas?.Count ?? 0} amounts): " +
				(changed ? $"revision {State.Revision}, {entries.Count} entries." : "nothing changed."));
			if (refusal != null) Logger.Info($"[ShopList] Refused for client {clientId}: {refusal}");
		}

		private static string Apply(List<ShopListEntry> entries, ShopListEntry delta)
		{
			if (delta == null || string.IsNullOrEmpty(delta.Id)) return "no item id";
			if (delta.Amount == 0) return "no amount";
			int index = entries.FindIndex(e => e.SameItem(delta));
			if (index < 0)
			{
				if (delta.Amount < 0) return "not on the list";
				if (entries.Count >= ShopListState.MaxEntries) return $"the list already has {ShopListState.MaxEntries} entries";
				entries.Add(delta.WithAmount(Math.Min(delta.Amount, ShopListState.MaxAmount)));
				return delta.Amount > ShopListState.MaxAmount ? $"capped at {ShopListState.MaxAmount}" : null;
			}

			int current = entries[index].Amount;
			int amount = current + delta.Amount;
			if (amount <= 0)
			{
				entries.RemoveAt(index);
				return amount < 0 ? $"only {current} on the list" : null;
			}
			entries[index] = entries[index].WithAmount(Math.Min(amount, ShopListState.MaxAmount));
			return amount > ShopListState.MaxAmount ? $"capped at {ShopListState.MaxAmount}" : null;
		}

		public static int SendSnapshot(int clientId)
		{
			Server.SendToClient(Snapshot(ShopListStatePacket.FromServer, 0, null), clientId);
			return 1;
		}

		private static ShopListStatePacket Snapshot(int sourcePlayer, int sourceSeq, string refused) => new ShopListStatePacket
		{
			Entries = State.Entries.Select(e => e.WithAmount(e.Amount)).ToList(),
			Revision = State.Revision,
			SourcePlayer = sourcePlayer,
			SourceSeq = sourceSeq,
			Refused = refused
		};

		public static IEnumerable<string> Describe()
		{
			yield return $"revision {State.Revision}, {State.Entries.Count} entries";
			foreach (var entry in State.Entries) yield return entry.Describe();
		}
	}
}
