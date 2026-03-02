using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Collodion
{
    public sealed partial class BlockEntityPlateBox : BlockEntity
    {
        private const string BlockSlotPrefix = "collodionPlateBoxSlot";
        private const string ItemSlotPrefix = "collodionPlateBoxItemSlot";
        private const string BlockOpenAttr = "collodionPlateBoxOpen";

        public const int SlotCount = 8;

        private readonly object slotLock = new object();
        private readonly ItemStack?[] plateSlots = new ItemStack?[SlotCount];
        private bool isOpen;

        partial void ClientInitialize(ICoreAPI api);
        partial void ClientSlotsChanged(bool markBlockDirty);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            ClientInitialize(api);
        }

        public bool HasPlateAt(int slotIndex)
        {
            if ((uint)slotIndex >= SlotCount) return false;

            lock (slotLock)
            {
                return plateSlots[slotIndex] != null;
            }
        }

        public bool IsOpen
        {
            get
            {
                lock (slotLock)
                {
                    return isOpen;
                }
            }
        }

        public bool SetOpen(bool open)
        {
            bool changed;

            lock (slotLock)
            {
                changed = isOpen != open;
                isOpen = open;
            }

            if (changed)
            {
                OnSlotsChanged();
            }

            return changed;
        }

        public bool ToggleOpen()
        {
            bool nextOpen;

            lock (slotLock)
            {
                nextOpen = !isOpen;
            }

            SetOpen(nextOpen);
            return nextOpen;
        }

        public bool CanInsertAt(int slotIndex)
        {
            if ((uint)slotIndex >= SlotCount) return false;

            lock (slotLock)
            {
                return plateSlots[slotIndex] == null;
            }
        }

        public int GetFirstEmptySlot()
        {
            lock (slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    if (plateSlots[index] == null) return index;
                }
            }

            return -1;
        }

        public int GetFirstFilledSlot()
        {
            lock (slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    if (plateSlots[index] != null) return index;
                }
            }

            return -1;
        }

        public int GetUsedSlotCount()
        {
            int count = 0;

            lock (slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    if (plateSlots[index] != null) count++;
                }
            }

            return count;
        }

        public bool TryInsertPlateAt(int slotIndex, ItemStack stack, IWorldAccessor world)
        {
            if ((uint)slotIndex >= SlotCount || stack == null || !IsInsertablePlate(stack)) return false;

            ItemStack insertStack = stack.Clone();
            insertStack.StackSize = 1;
            WetPlateAttrs.PauseWetTimerForStorage(world, insertStack);

            lock (slotLock)
            {
                if (plateSlots[slotIndex] != null) return false;
                plateSlots[slotIndex] = insertStack;
            }

            OnSlotsChanged();
            return true;
        }

        public ItemStack? TakePlateAt(int slotIndex, IWorldAccessor world)
        {
            if ((uint)slotIndex >= SlotCount) return null;

            ItemStack? stack;
            lock (slotLock)
            {
                stack = plateSlots[slotIndex];
                if (stack == null) return null;
                plateSlots[slotIndex] = null;
            }

            ItemStack output = stack.Clone();
            WetPlateAttrs.ResumeWetTimerFromStorage(world, output);

            OnSlotsChanged();
            return output;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            WriteSlotsToAttributes(tree, BlockSlotPrefix);
            tree.SetBool(BlockOpenAttr, IsOpen);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            lock (slotLock)
            {
                isOpen = tree.GetBool(BlockOpenAttr, false);
            }

            ReadSlotsFromAttributes(tree, BlockSlotPrefix, worldAccessForResolve);
        }

        public void SaveToItemStack(ItemStack target)
        {
            if (target?.Attributes == null) return;
            WriteSlotsToAttributes(target.Attributes, ItemSlotPrefix);
        }

        public void LoadFromItemStack(ItemStack source, IWorldAccessor world)
        {
            if (source?.Attributes == null) return;
            ReadSlotsFromAttributes(source.Attributes, ItemSlotPrefix, world);
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine($"Plate slots: {GetUsedSlotCount()}/{SlotCount}");
        }

        public static bool IsInsertablePlate(ItemStack? stack)
        {
            return stack?.Item is ItemPlateBase;
        }

        private void WriteSlotsToAttributes(ITreeAttribute attrs, string prefix)
        {
            lock (slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    string key = prefix + index;
                    ItemStack? slot = plateSlots[index];
                    if (slot != null) attrs.SetItemstack(key, slot);
                    else attrs.RemoveAttribute(key);
                }
            }
        }

        private void ReadSlotsFromAttributes(ITreeAttribute attrs, string prefix, IWorldAccessor world)
        {
            lock (slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    string key = prefix + index;
                    ItemStack? loaded = attrs.GetItemstack(key, null);
                    loaded?.ResolveBlockOrItem(world);
                    if (loaded != null && !IsInsertablePlate(loaded)) loaded = null;
                    if (loaded != null) loaded.StackSize = 1;
                    plateSlots[index] = loaded;
                }
            }

            OnSlotsChanged();
        }

        private void OnSlotsChanged()
        {
            ClientSlotsChanged(markBlockDirty: true);
            MarkDirty(true);
        }
    }
}
