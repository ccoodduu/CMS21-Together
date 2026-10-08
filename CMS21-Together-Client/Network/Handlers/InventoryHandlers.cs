using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;

namespace CMS21Together.Network.Handlers
{
    public static class InventoryHandlers
    {
        private static System.Collections.Generic.Queue<(InventorySyncPacket Packet, int SnapshotId)> syncQueue = new System.Collections.Generic.Queue<(InventorySyncPacket, int)>();
        private static bool isProcessingSync = false;
        private static readonly System.Collections.Generic.Queue<System.Action> heldDuringFullSync = new System.Collections.Generic.Queue<System.Action>();
        private static bool fullSyncOpen;

        public static bool FullSyncOpen => fullSyncOpen;
        private static bool waitingForInventory;
        private static bool replaying;

        public static bool IgnoreInventoryHooks = false;

        // Also while a scene loads: GameManager's inventory is gone between the old scene and the new one.
        public static bool HoldUntilReady(System.Action apply)
        {
            if (!fullSyncOpen && (replaying || heldDuringFullSync.Count == 0) && InventoryAvailable()) return false;
            heldDuringFullSync.Enqueue(apply);
            if (!fullSyncOpen && !waitingForInventory)
            {
                waitingForInventory = true;
                MelonLoader.MelonCoroutines.Start(ReplayWhenAvailable());
            }
            return true;
        }

        private static bool InventoryAvailable()
        {
            var manager = Singleton<GameManager>.Instance;
            return manager != null && manager.Inventory != null && manager.Warehouse != null;
        }

        private static System.Collections.IEnumerator ReplayWhenAvailable()
        {
            while (fullSyncOpen || !InventoryAvailable()) yield return null;
            waitingForInventory = false;
            ReplayHeld();
        }

        public static void ResetHeld()
        {
            heldDuringFullSync.Clear();
            fullSyncOpen = false;
        }

        private static void ReplayHeld()
        {
            if (heldDuringFullSync.Count > 0) Log.Debug($"[InventoryHandlers] Replaying {heldDuringFullSync.Count} inventory changes held during a full sync or a scene load.");
            replaying = true;
            try
            {
                while (heldDuringFullSync.Count > 0)
                {
                    var apply = heldDuringFullSync.Dequeue();
                    try
                    {
                        apply();
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[InventoryHandlers] Replaying an inventory change failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                replaying = false;
            }
        }

        [PacketHandler(PacketTypes.InventoryData)]
        public static void HandleInventorySync(long clientId, InventorySyncPacket packet)
        {
            fullSyncOpen = true;
            syncQueue.Enqueue((packet, SyncTracker.ReceivingSnapshotId));
            SyncTracker.MarkProgress();
            if (!isProcessingSync)
            {
                isProcessingSync = true;
                MelonLoader.MelonCoroutines.Start(ProcessSyncQueue());
            }
        }

        private static System.Collections.IEnumerator ProcessSyncQueue()
        {
            // Wait for the garage state to be fully synced and ready
            while (!ClientData.IsGarageStateSynced)
            {
                yield return new UnityEngine.WaitForSeconds(0.1f);
            }

            while (syncQueue.Count > 0)
            {
                var (packet, snapshotId) = syncQueue.Dequeue();

                IgnoreInventoryHooks = true;

                if (packet.IsFirstBatch)
                {
                    // Clear all local inventories first to prevent duplication
                    Singleton<GameManager>.Instance.Inventory.DeleteAllInventory();
                    
                    var warehouse = Singleton<GameManager>.Instance.Warehouse;
                    if (warehouse != null)
                    {
                        var allWhItems = warehouse.SortItemsForCategory(global::SortType.ByAlphabetAsc, CMS.UI.Logic.InventoryCategories.All);
                        if (allWhItems != null)
                        {
                            foreach (var baseItem in allWhItems)
                            {
                                var i = baseItem.TryCast<Item>();
                                if (i != null) warehouse.Delete(i);
                                else
                                {
                                    var gi = baseItem.TryCast<GroupItem>();
                                    if (gi != null) warehouse.Delete(gi);
                                }
                            }
                        }
                    }

                    Log.Debug("[InventoryHandlers] Cleared local inventory and warehouse for full sync.");
                }

                int count = 0;
                if (packet.InventoryItems != null)
                {
                    foreach (var item in packet.InventoryItems)
                    {
                        Singleton<GameManager>.Instance.Inventory.Add(item.ToGameItem());
                        count++;
                    }
                }
                if (packet.InventoryGroupItems != null)
                {
                    foreach (var group in packet.InventoryGroupItems)
                    {
                        Singleton<GameManager>.Instance.Inventory.AddGroup(group.ToGameGroupItem());
                        count++;
                    }
                }
                if (packet.WarehouseItems != null)
                {
                    foreach (var item in packet.WarehouseItems)
                    {
                        Singleton<GameManager>.Instance.Warehouse.Add(item.ToGameItem());
                        count++;
                    }
                }
                if (packet.WarehouseGroupItems != null)
                {
                    foreach (var group in packet.WarehouseGroupItems)
                    {
                        Singleton<GameManager>.Instance.Warehouse.Add(group.ToGameGroupItem());
                        count++;
                    }
                }
                
                IgnoreInventoryHooks = false;
                
                Log.Debug($"[InventoryHandlers] Received batch containing {count} items. LastBatch={packet.IsLastBatch}");

                if (packet.IsLastBatch)
                {
                    Log.Success("[InventoryHandlers] Inventory sync complete!");
                    ClientData.IsInventorySynced = true;
                    if (syncQueue.Count == 0)
                    {
                        fullSyncOpen = false;
                        ReplayHeld();
                        Logic.Car.Parts.PartTransactions.ReapplyOpen();
                    }
                    RefreshInventoryWindow();
                    RefreshWarehouseWindow();
                }

                SyncTracker.Applied(CMS21_Together_Core.Data.SyncOrder.InventoryKey, snapshotId);

                yield return null; // wait a frame between batches
            }

            isProcessingSync = false;
        }

        [PacketHandler(PacketTypes.InventoryItemAction)]
        public static void HandleInventoryItemAction(long clientId, InventoryItemActionPacket packet)
        {
            if (HoldUntilReady(() => HandleInventoryItemAction(clientId, packet))) return;
            IgnoreInventoryHooks = true;
            try
            {
                var inventory = Singleton<GameManager>.Instance.Inventory;
                if (packet.Action == ItemActionType.Add)
                {
                    if (inventory.GetItem(packet.Item.UID) == null)
                        inventory.Add(packet.Item.ToGameItem());
                }
                else if (packet.Action == ItemActionType.Remove)
                {
                    var item = inventory.GetItem(packet.Item.UID);
                    if (item != null && Logic.Car.Parts.PartTransactions.HoldsAdd(packet.Item.UID))
                        Log.Info($"[InventoryHandlers] Remove of item {packet.Item.UID} skipped: an open part change added it and has not been sent yet.");
                    else if (item != null)
                        inventory.Delete(item);
                }
                else if (packet.Action == ItemActionType.Update)
                {
                    var item = inventory.GetItem(packet.Item.UID);
                    if (item != null) ItemConverter.CopyInto(packet.Item, item);
                    else Log.Debug($"[InventoryHandlers] Update of item {packet.Item.UID} ignored, not in the inventory.");
                }
            }
            finally
            {
                IgnoreInventoryHooks = false;
            }
            
            RefreshInventoryWindow();
        }

        [PacketHandler(PacketTypes.InventoryGroupItemAction)]
        public static void HandleInventoryGroupItemAction(long clientId, InventoryGroupItemActionPacket packet)
        {
            if (HoldUntilReady(() => HandleInventoryGroupItemAction(clientId, packet))) return;
            IgnoreInventoryHooks = true;
            try
            {
                if (packet.Action == ItemActionType.Add)
                {
                    if (Singleton<GameManager>.Instance.Inventory.GetGroup(packet.GroupItem.UID) == null)
                        Singleton<GameManager>.Instance.Inventory.AddGroup(packet.GroupItem.ToGameGroupItem());
                }
                else if (packet.Action == ItemActionType.Remove)
                {
                    Singleton<GameManager>.Instance.Inventory.DeleteGroup(packet.GroupItem.UID);
                }
                else if (packet.Action == ItemActionType.Update && Singleton<GameManager>.Instance.Inventory.GetGroup(packet.GroupItem.UID) != null)
                {
                    Singleton<GameManager>.Instance.Inventory.DeleteGroup(packet.GroupItem.UID);
                    Singleton<GameManager>.Instance.Inventory.AddGroup(packet.GroupItem.ToGameGroupItem());
                }
            }
            finally
            {
                IgnoreInventoryHooks = false;
            }
            
            RefreshInventoryWindow();
        }

        [PacketHandler(PacketTypes.WarehouseAction)]
        public static void HandleWarehouseAction(long clientId, WarehouseActionPacket packet)
        {
            if (HoldUntilReady(() => HandleWarehouseAction(clientId, packet))) return;
            IgnoreInventoryHooks = true;
            try
            {
                if (packet.ToWarehouse)
                {
                    // Remove from Inventory, Add to Warehouse
                    if (packet.IsGroupItem)
                    {
                        Singleton<GameManager>.Instance.Inventory.DeleteGroup(packet.GroupItem.UID); 
                        if (!WarehouseHas(packet.GroupItem.UID))
                            Singleton<GameManager>.Instance.Warehouse.Add(packet.GroupItem.ToGameGroupItem());
                    }
                    else
                    {
                        var item = Singleton<GameManager>.Instance.Inventory.GetItem(packet.Item.UID);
                        if (item != null)
                            Singleton<GameManager>.Instance.Inventory.Delete(item);
                        if (!WarehouseHas(packet.Item.UID))
                            Singleton<GameManager>.Instance.Warehouse.Add(packet.Item.ToGameItem());
                    }
                }
                else
                {
                    // From Warehouse to Inventory
                    if (packet.IsGroupItem)
                    {
                        var grp = packet.GroupItem.ToGameGroupItem();
                        Singleton<GameManager>.Instance.Warehouse.Delete(grp);
                        if (Singleton<GameManager>.Instance.Inventory.GetGroup(grp.UID) == null)
                            Singleton<GameManager>.Instance.Inventory.AddGroup(grp);
                    }
                    else
                    {
                        var item = packet.Item.ToGameItem();
                        Singleton<GameManager>.Instance.Warehouse.Delete(item);
                        if (Singleton<GameManager>.Instance.Inventory.GetItem(item.UID) == null)
                            Singleton<GameManager>.Instance.Inventory.Add(item);
                    }
                }
            }
            finally
            {
                IgnoreInventoryHooks = false;
            }
            
            RefreshInventoryWindow();
            RefreshWarehouseWindow();
        }

        private static bool WarehouseHas(long uid)
        {
            var all = Singleton<GameManager>.Instance.Warehouse?.GetAllItemsAndGroups();
            for (int i = 0; all != null && i < all.Count; i++)
                if (all[i].UID == uid) return true;
            return false;
        }

        public static void RefreshInventoryWindow()
        {
            try
            {
                if (WindowManager.Instance != null)
                {
                    var invWindow = WindowManager.Instance.GetWindowByID<InventoryWindow>(WindowID.Inventory);
                    if (invWindow != null && invWindow.isActive)
                    {
                        invWindow.Refresh(true);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[InventoryHandlers] Error refreshing InventoryWindow: {ex.Message}");
            }
        }
        
        public static void RefreshWarehouseWindow()
        {
            try
            {
                if (WindowManager.Instance != null)
                {
                    var whWindow = WindowManager.Instance.GetWindowByID<WarehouseWindow>(WindowID.Warehouse);
                    if (whWindow != null && whWindow.isActive)
                    {
                        whWindow.Refresh(true);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[InventoryHandlers] Error refreshing WarehouseWindow: {ex.Message}");
            }
        }
    }
}
