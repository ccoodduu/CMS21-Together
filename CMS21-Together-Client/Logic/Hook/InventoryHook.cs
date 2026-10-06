using System;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Tools;
using CMS21Together.Network;
using CMS21Together.Network.Handlers;
using HarmonyLib;

namespace CMS21Together.Logic.Hook
{
    [HarmonyPatch]
    public static class InventoryHook
    {
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Add), new Type[] { typeof(Item), typeof(bool) })]
        [HarmonyPrefix]
        public static bool AddItemPrefix(Item item, bool showPopup)
        {
            if (ToolSync.BlockInventoryCall(item.UID, "Add", item.ID)) return false;
            if (!ClientScene.IsGarageReady) return true;
            if (Client.Instance.IsConnected && !InventoryHandlers.IgnoreInventoryHooks && PartTransactions.SuppressAdd(item.ID)) return false;
            if (Client.Instance.IsConnected && !InventoryHandlers.IgnoreInventoryHooks && !PartTransactions.CaptureAdd(item.ToModItem()))
            {
                var packet = new InventoryItemActionPacket
                {
                    Action = ItemActionType.Add,
                    Item = item.ToModItem()
                };
                Client.Instance.Send(packet);
            }
            return true;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Delete), new Type[] { typeof(Item) })]
        [HarmonyPrefix]
        public static bool DeleteItemPrefix(Item item)
        {
            if (ToolSync.BlockInventoryCall(item.UID, "Delete", item.ID)) return false;
            if (!ClientScene.IsGarageReady) return true;
            if (Client.Instance.IsConnected && !InventoryHandlers.IgnoreInventoryHooks && !PartTransactions.CaptureDelete(item.ToModItem()))
            {
                var packet = new InventoryItemActionPacket
                {
                    Action = ItemActionType.Remove,
                    Item = item.ToModItem()
                };
                Client.Instance.Send(packet);
            }
            return true;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddGroup), new Type[] { typeof(GroupItem) })]
        [HarmonyPrefix]
        public static bool AddGroupItemPrefix(GroupItem group)
        {
            if (ToolSync.BlockInventoryCall(group.UID, "AddGroup", group.ID)) return false;
            if (!ClientScene.IsGarageReady) return true;
            if (Client.Instance.IsConnected && !InventoryHandlers.IgnoreInventoryHooks && !PartTransactions.CaptureAddGroup(group.ToModGroupItem()))
            {
                var packet = new InventoryGroupItemActionPacket
                {
                    Action = ItemActionType.Add,
                    GroupItem = group.ToModGroupItem()
                };
                Client.Instance.Send(packet);
            }
            return true;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.DeleteGroup), new Type[] { typeof(long) })]
        [HarmonyPrefix]
        public static bool DeleteGroupItemPrefix(long UId)
        {
            if (ToolSync.BlockInventoryCall(UId, "DeleteGroup", null)) return false;
            if (!ClientScene.IsGarageReady) return true;
            bool tracked = Client.Instance.IsConnected && !InventoryHandlers.IgnoreInventoryHooks;
            var existing = tracked ? EngineCraneHooks.TakeInsertedGroup(UId) ?? Singleton<GameManager>.Instance.Inventory.GetGroup(UId).ToModGroupItem() : null;
            if (tracked && (existing == null || !PartTransactions.CaptureDeleteGroup(existing)))
            {
                // We only need the UID to delete it
                var packet = new InventoryGroupItemActionPacket
                {
                    Action = ItemActionType.Remove,
                    GroupItem = new CMS21_Together_Core.Data.GameType.ModGroupItem { UID = UId }
                };
                Client.Instance.Send(packet);
            }
            return true;
        }
    }
}
