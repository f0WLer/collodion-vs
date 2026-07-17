using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Photocore.Plates;

namespace Photocore.PlateBox
{
    public sealed partial class BlockEntityPlateBox : BlockEntity
    {
        private const string BlockSlotPrefix = "photochemPlateBoxSlot";
        private const string ItemSlotPrefix = "photochemPlateBoxItemSlot";
        private const string BlockOpenAttr = "photochemPlateBoxOpen";

        public const int SlotCount = 8;

        private readonly object _slotLock = new();
        private readonly ItemStack?[] _plateSlots = new ItemStack?[SlotCount];
        private bool _isOpen;

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

            lock (_slotLock)
            {
                return _plateSlots[slotIndex] != null;
            }
        }

        public bool IsOpen
        {
            get
            {
                lock (_slotLock)
                {
                    return _isOpen;
                }
            }
        }

        internal bool SetOpen(bool open)
        {
            bool changed;

            lock (_slotLock)
            {
                changed = _isOpen != open;
                _isOpen = open;
            }

            if (changed)
            {
                OnSlotsChanged();
            }

            return changed;
        }

        public int PlateCount => GetUsedSlotCount();

        private int GetUsedSlotCount()
        {
            int count = 0;

            lock (_slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    if (_plateSlots[index] != null) count++;
                }
            }

            return count;
        }

        // Inserts one plate into a slot. While stored the stack carries a marker that
        // ItemPlateBase.GetTransitionRateMul reads to apply the configured plate-box
        // drying multiplier (default 0 = full pause).
        internal bool TryInsertPlateAt(int slotIndex, ItemStack stack, IWorldAccessor world)
        {
            if ((uint)slotIndex >= SlotCount || stack == null || !IsInsertablePlate(stack)) return false;

            ItemStack insertStack = stack.Clone();
            insertStack.StackSize = 1;
            // Roll forward any pending decay before flipping into storage mode so the
            // pre-storage period is accounted for at the open-air rate.
            PlateDryingTransition.TickNow(world, insertStack);
            PlateDryingTransition.SetStoredInPlateBox(insertStack, true);

            lock (_slotLock)
            {
                if (_plateSlots[slotIndex] != null) return false;
                _plateSlots[slotIndex] = insertStack;
            }

            OnSlotsChanged();
            return true;
        }

        // Removes one stored plate from a slot. The storage marker is cleared after a
        // final tick so any in-storage drying (when the multiplier is non-zero) is applied.
        internal ItemStack? TakePlateAt(int slotIndex, IWorldAccessor world)
        {
            if ((uint)slotIndex >= SlotCount) return null;

            ItemStack? stack;
            lock (_slotLock)
            {
                stack = _plateSlots[slotIndex];
                if (stack == null) return null;
                _plateSlots[slotIndex] = null;
            }

            ItemStack output = stack.Clone();
            // Tick once more while the storage flag is still set, then clear it so the
            // stack ages at the normal rate from now on.
            PlateDryingTransition.TickNow(world, output);
            PlateDryingTransition.SetStoredInPlateBox(output, false);

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

            lock (_slotLock)
            {
                _isOpen = tree.GetBool(BlockOpenAttr, false);
            }

            ReadSlotsFromAttributes(tree, BlockSlotPrefix, worldAccessForResolve);
        }

        internal void SaveToItemStack(ItemStack target)
        {
            if (target?.Attributes == null) return;
            WriteSlotsToAttributes(target.Attributes, ItemSlotPrefix);
        }

        internal void LoadFromItemStack(ItemStack source, IWorldAccessor world)
        {
            if (source?.Attributes == null) return;
            ReadSlotsFromAttributes(source.Attributes, ItemSlotPrefix, world);
        }

        // Reads how many plates a held (not yet placed) box is carrying straight off the item stack's
        // attributes -- the same ItemSlotPrefix keys SaveToItemStack/LoadFromItemStack use -- so callers
        // like the walk-sound hook don't need a block entity for a stack that isn't placed anywhere.
        public static int GetPlateCountFromItemStack(ItemStack? stack)
        {
            if (stack?.Attributes == null) return 0;

            int count = 0;
            for (int index = 0; index < SlotCount; index++)
            {
                if (stack.Attributes.HasAttribute(ItemSlotPrefix + index)) count++;
            }

            return count;
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("photocore:platebox-info-slots", GetUsedSlotCount(), SlotCount));
        }

        public static bool IsInsertablePlate(ItemStack? stack)
        {
            return stack?.Item is ItemPlateBase;
        }

        private void WriteSlotsToAttributes(ITreeAttribute attrs, string prefix)
        {
            lock (_slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    string key = prefix + index;
                    ItemStack? slot = _plateSlots[index];
                    if (slot != null) attrs.SetItemstack(key, slot);
                    else attrs.RemoveAttribute(key);
                }
            }
        }

        private void ReadSlotsFromAttributes(ITreeAttribute attrs, string prefix, IWorldAccessor world)
        {
            lock (_slotLock)
            {
                for (int index = 0; index < SlotCount; index++)
                {
                    string key = prefix + index;
                    ItemStack? loaded = attrs.TryGetAttribute(key, out IAttribute raw) && raw is ItemstackAttribute isa ? isa.value : null;
                    loaded?.ResolveBlockOrItem(world);
                    if (loaded != null && !IsInsertablePlate(loaded)) loaded = null;
                    if (loaded != null) loaded.StackSize = 1;
                    _plateSlots[index] = loaded;
                }
            }

            OnSlotsChanged();
        }

        // Centralizes synchronization after any slot/open-state mutation.
        private void OnSlotsChanged()
        {
            ClientSlotsChanged(markBlockDirty: true);
            MarkDirty(true);
        }
    }
}
