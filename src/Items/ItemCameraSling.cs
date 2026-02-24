using System;
using System.Collections;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public class ItemCameraSling : Item
    {
        public const string AttrStoredCameraStack = "collodionStoredCameraStack";
        private static readonly AssetLocation CameraSlingFullCode = new AssetLocation("collodion", "camerasling-full");
        private static readonly AssetLocation CameraSlingWallCode = new AssetLocation("collodion", "cameraslingwall");

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);

            if (slot?.Itemstack?.Item is not ItemCameraSling) return;

            if (byEntity is not EntityPlayer entityPlayer) return;
            IPlayer? player = entityPlayer.Player;
            if (player == null) return;

            bool shiftDown = IsShiftDown(entityPlayer);
            bool ctrlDown = IsCtrlDown(entityPlayer);

            // Shift+Ctrl+RMB with a full sling in hand: nail sling to wall.
            if (shiftDown && ctrlDown && IsFullSling(slot.Itemstack))
            {
                handling = EnumHandHandling.PreventDefault;

                if (api?.Side != EnumAppSide.Server) return;

                TryPlaceOnWall(slot, blockSel);
                return;
            }

            // Plain RMB with sling in hand: equip into left shoulder slot.
            handling = EnumHandHandling.PreventDefault;
            if (api?.Side != EnumAppSide.Server) return;

            TryEquipToLeftShoulder(player, slot);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            dsc.AppendLine("Wear in left shoulder slot.");
            dsc.AppendLine("Press R to store/unstore camera from active slot.");
            dsc.AppendLine("Right click: wear instantly.");
            dsc.AppendLine("Shift+Ctrl+Right click on wall: mount sling.");

            ItemStack? stored = null;
            try
            {
                stored = inSlot?.Itemstack?.Attributes?.GetItemstack(AttrStoredCameraStack, null);
            }
            catch
            {
                stored = null;
            }

            dsc.AppendLine(stored == null ? "Stored camera: (none)" : "Stored camera: loaded");
        }

        private static bool IsFullSling(ItemStack? stack)
        {
            return stack?.Collectible?.Code == CameraSlingFullCode;
        }

        private static bool IsShiftDown(EntityPlayer entityPlayer)
        {
            var controls = entityPlayer?.Controls;
            if (controls == null) return false;

            try
            {
                if (controls.ShiftKey) return true;
            }
            catch
            {
                // ignore
            }

            try
            {
                if (controls.Sneak) return true;
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool IsCtrlDown(EntityPlayer entityPlayer)
        {
            object? controls = entityPlayer?.Controls;
            if (controls == null) return false;

            try
            {
                object? ctrl = controls.GetType().GetProperty("CtrlKey")?.GetValue(controls)
                    ?? controls.GetType().GetProperty("ControlKey")?.GetValue(controls)
                    ?? controls.GetType().GetField("CtrlKey")?.GetValue(controls)
                    ?? controls.GetType().GetField("ControlKey")?.GetValue(controls);

                if (ctrl is bool b) return b;
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool IsLeftShoulderSlot(ItemSlot slot)
        {
            var slotTypeProp = slot.GetType().GetProperty("SlotType");
            if (slotTypeProp?.GetValue(slot) is string slotType)
            {
                return string.Equals(slotType, "leftshouldergear", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool InventoryHasLeftShoulderSlot(InventoryBase inventory)
        {
            foreach (ItemSlot? slot in inventory)
            {
                if (slot != null && IsLeftShoulderSlot(slot)) return true;
            }

            return false;
        }

        private static InventoryBase? GetGearInventory(IPlayer player)
        {
            object? invManager = player.InventoryManager;
            if (invManager == null) return null;

            var getOwnInventoryMethod = invManager.GetType().GetMethod("GetOwnInventory", new[] { typeof(string) });
            if (getOwnInventoryMethod != null)
            {
                string[] candidates = { "character", "gear", "clothes" };
                foreach (string className in candidates)
                {
                    if (getOwnInventoryMethod.Invoke(invManager, new object[] { className }) is InventoryBase inventory && InventoryHasLeftShoulderSlot(inventory))
                    {
                        return inventory;
                    }
                }
            }

            var inventoriesProp = invManager.GetType().GetProperty("Inventories");
            if (inventoriesProp?.GetValue(invManager) is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Value is InventoryBase inventory && InventoryHasLeftShoulderSlot(inventory))
                    {
                        return inventory;
                    }
                }
            }

            return null;
        }

        private static ItemSlot? GetLeftShoulderSlot(IPlayer player)
        {
            InventoryBase? gearInventory = GetGearInventory(player);
            if (gearInventory == null) return null;

            foreach (ItemSlot? slot in gearInventory)
            {
                if (slot == null || !IsLeftShoulderSlot(slot)) continue;
                return slot;
            }

            return null;
        }

        private static bool TryEquipToLeftShoulder(IPlayer player, ItemSlot handSlot)
        {
            ItemSlot? shoulderSlot = GetLeftShoulderSlot(player);
            if (shoulderSlot == null) return false;
            if (!shoulderSlot.Empty) return false;

            ItemStack? move = handSlot.TakeOutWhole();
            if (move == null) return false;

            shoulderSlot.Itemstack = move;
            shoulderSlot.MarkDirty();
            handSlot.MarkDirty();
            return true;
        }

        private bool TryPlaceOnWall(ItemSlot handSlot, BlockSelection? blockSel)
        {
            if (api?.World == null || blockSel?.Position == null || blockSel.Face == null) return false;
            if (!IsFullSling(handSlot.Itemstack)) return false;

            BlockFacing face = blockSel.Face;
            if (!face.IsHorizontal) return false;

            BlockPos targetPos = blockSel.Position;
            BlockPos placePos = targetPos.AddCopy(face);
            string orientation = face.Opposite.Code;

            Block? wallBlock = api.World.GetBlock(new AssetLocation(CameraSlingWallCode.Domain, $"{CameraSlingWallCode.Path}-{orientation}"));
            if (wallBlock == null || wallBlock.Id <= 0) return false;

            Block existing = api.World.BlockAccessor.GetBlock(placePos);
            if (existing != null && existing.Id != 0 && !existing.IsReplacableBy(wallBlock)) return false;

            ItemStack? slingStack = handSlot.Itemstack;
            if (slingStack == null) return false;

            ItemStack mountedSling = slingStack.Clone();
            mountedSling.StackSize = 1;

            api.World.BlockAccessor.SetBlock(wallBlock.Id, placePos);

            if (api.World.BlockAccessor.GetBlockEntity(placePos) is not BlockEntityWallMountedCameraSling be)
            {
                api.World.BlockAccessor.SetBlock(0, placePos);
                return false;
            }

            be.SetSlingStack(mountedSling);

            handSlot.TakeOut(1);
            handSlot.MarkDirty();
            return true;
        }
    }
}
