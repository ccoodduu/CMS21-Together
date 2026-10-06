using System;
using System.Linq;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Economy;
using CMS21_Together_Server.Log;

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
                if (state.InventoryState.InventoryItems.Any(i => i.UID == packet.Item.UID))
                {
                    Logger.Debug($"[Inventory] ADD of item {packet.Item.UID} from client {clientId} ignored, already present.");
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
                    Server.SendToClients(packet, (int)clientId);
                    EconomyService.OnInventoryRemoved((int)clientId, packet.Item);
                }
            }
            else if (packet.Action == ItemActionType.Update)
            {
                int index = state.InventoryState.InventoryItems.FindIndex(i => i.UID == packet.Item.UID);
                if (index < 0)
                {
                    Logger.Info($"[Inventory] Update of item {packet.Item.UID} from client {clientId} ignored, not in the inventory.");
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
                if (state.InventoryState.InventoryGroupItems.Any(g => g.UID == packet.GroupItem.UID))
                {
                    Logger.Debug($"[Inventory] ADD of group {packet.GroupItem.UID} from client {clientId} ignored, already present.");
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
                        state.InventoryState.WarehouseGroupItems.Add(packet.GroupItem);
                        Server.SendToClients(packet); // Send to all including sender so sender can update their local UI
                    }
                }
                else
                {
                    if (state.InventoryState.InventoryItems.RemoveAll(i => i.UID == packet.Item.UID) > 0)
                    {
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
        
        public static long GenerateNewUID()
        {
            return DateTime.UtcNow.Ticks;
        }
    }
}
