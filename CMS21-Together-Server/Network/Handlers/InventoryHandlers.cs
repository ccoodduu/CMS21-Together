using System;
using System.Linq;
using System.Threading;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Economy;
using CMS21_Together_Server.Log;
using Newtonsoft.Json;

namespace CMS21_Together_Server.Network.Handlers
{
    public static class InventoryHandlers
    {
        [PacketHandler(PacketTypes.InventoryItemAction)]
        public static void HandleInventoryItemAction(long clientId, InventoryItemActionPacket packet)
        {
            var state = GameDataManager.CurrentState;
            if (packet.Action == ItemActionType.Add)
            {
                var stored = state.InventoryState.InventoryItems.FirstOrDefault(i => i.UID == packet.Item.UID);
                if (stored != null)
                {
                    bool differs = !SameJson(stored, packet.Item);
                    Logger.Debug($"[Inventory] ADD of item {packet.Item.UID} from client {clientId} ignored, already present{(differs ? "; answering with the stored copy" : "")}.");
                    if (differs) Server.SendToClient(new InventoryItemActionPacket { Action = ItemActionType.Update, Item = stored }, (int)clientId);
                    return;
                }
                state.InventoryState.InventoryItems.Add(packet.Item);
                Server.SendToClients(packet, (int)clientId);
                EconomyService.OnInventoryAdded(packet.Item);
            }
            else if (packet.Action == ItemActionType.Remove)
            {
                var removed = state.InventoryState.InventoryItems.RemoveAll(i => i.UID == packet.Item.UID) > 0;
                if (removed)
                {
                    InventoryChanges.NoteRemoved(packet.Item.UID, (int)clientId);
                    Server.SendToClients(packet, (int)clientId);
                    EconomyService.OnInventoryRemoved((int)clientId, packet.Item);
                }
            }
            else if (packet.Action == ItemActionType.Update)
            {
                int index = state.InventoryState.InventoryItems.FindIndex(i => i.UID == packet.Item.UID);
                if (index < 0)
                {
                    Logger.Info($"[Inventory] Update of item {packet.Item.UID} from client {clientId} ignored, not in the inventory ({InventoryChanges.DescribeRemover(packet.Item.UID)}); answering with its Remove.");
                    Server.SendToClient(new InventoryItemActionPacket { Action = ItemActionType.Remove, Item = packet.Item }, (int)clientId);
                    return;
                }
                state.InventoryState.InventoryItems[index] = packet.Item;
                Server.SendToClients(packet, (int)clientId);
            }
        }

        [PacketHandler(PacketTypes.InventoryGroupItemAction)]
        public static void HandleInventoryGroupItemAction(long clientId, InventoryGroupItemActionPacket packet)
        {
            var state = GameDataManager.CurrentState;
            if (packet.Action == ItemActionType.Add)
            {
                var stored = state.InventoryState.InventoryGroupItems.FirstOrDefault(g => g.UID == packet.GroupItem.UID);
                if (stored != null)
                {
                    bool differs = !SameJson(GroupContent(stored), GroupContent(packet.GroupItem));
                    Logger.Debug($"[Inventory] ADD of group {packet.GroupItem.UID} from client {clientId} ignored, already present{(differs ? "; answering with the stored copy" : "")}.");
                    if (differs) Server.SendToClient(new InventoryGroupItemActionPacket { Action = ItemActionType.Update, GroupItem = stored }, (int)clientId);
                    return;
                }
                state.InventoryState.InventoryGroupItems.Add(packet.GroupItem);
                Server.SendToClients(packet, (int)clientId);
            }
            else if (packet.Action == ItemActionType.Remove)
            {
                var removed = state.InventoryState.InventoryGroupItems.RemoveAll(i => i.UID == packet.GroupItem.UID) > 0;
                if (removed)
                {
                    InventoryChanges.NoteRemoved(packet.GroupItem.UID, (int)clientId);
                    Server.SendToClients(packet, (int)clientId);
                }
            }
        }

        [PacketHandler(PacketTypes.WarehouseAction)]
        public static void HandleWarehouseAction(long clientId, WarehouseActionPacket packet)
        {
            var state = GameDataManager.CurrentState;
            
            if (packet.ToWarehouse)
            {
                if (packet.IsGroupItem)
                {
                    if (state.InventoryState.InventoryGroupItems.RemoveAll(i => i.UID == packet.GroupItem.UID) > 0)
                    {
                        InventoryChanges.NoteRemoved(packet.GroupItem.UID, (int)clientId);
                        state.InventoryState.WarehouseGroupItems.Add(packet.GroupItem);
                        Server.SendToClients(packet); // Send to all including sender so sender can update their local UI
                    }
                }
                else
                {
                    if (state.InventoryState.InventoryItems.RemoveAll(i => i.UID == packet.Item.UID) > 0)
                    {
                        InventoryChanges.NoteRemoved(packet.Item.UID, (int)clientId);
                        state.InventoryState.WarehouseItems.Add(packet.Item);
                        Server.SendToClients(packet);
                    }
                }
            }
            else
            {
                // From warehouse to inventory
                if (packet.IsGroupItem)
                {
                    if (state.InventoryState.WarehouseGroupItems.RemoveAll(i => i.UID == packet.GroupItem.UID) > 0)
                    {
                        state.InventoryState.InventoryGroupItems.Add(packet.GroupItem);
                        Server.SendToClients(packet);
                    }
                }
                else
                {
                    if (state.InventoryState.WarehouseItems.RemoveAll(i => i.UID == packet.Item.UID) > 0)
                    {
                        state.InventoryState.InventoryItems.Add(packet.Item);
                        Server.SendToClients(packet);
                    }
                }
            }
        }
        
        private static bool SameJson(object stored, object incoming) =>
            JsonConvert.SerializeObject(stored) == JsonConvert.SerializeObject(incoming);

        private static object GroupContent(ModGroupItem group) => new { group.ID, group.UID, group.IsNormalGroup, group.Size, group.ItemList };

        private static long lastUid;

        public static long GenerateNewUID()
        {
            long uid, last;
            do
            {
                last = Interlocked.Read(ref lastUid);
                uid = Math.Max(DateTime.UtcNow.Ticks, last + 1);
            } while (Interlocked.CompareExchange(ref lastUid, uid, last) != last);
            return uid;
        }
    }
}
