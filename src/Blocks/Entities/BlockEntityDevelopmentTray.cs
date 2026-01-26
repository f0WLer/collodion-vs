using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Collodion
{
    public sealed partial class BlockEntityDevelopmentTray : BlockEntity
    {
        private const string AttrPlateStack = "collodionPlateStack";

        private readonly object plateLock = new object();

        public ItemStack? PlateStack { get; private set; }

        public bool HasPlate
        {
            get
            {
                lock (plateLock)
                {
                    return PlateStack != null;
                }
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            // If we already have a plate (loaded from disk), ensure the chunk is rebuilt so it shows.
            ClientPlateChanged(markBlockDirty: true);
        }

        public bool TryInsertPlate(ItemStack stack)
        {
            if (stack == null) return false;
            if (HasPlate) return false;

            lock (plateLock)
            {
                PlateStack = stack.Clone();
                PlateStack.StackSize = 1;
            }

            ClientPlateChanged(markBlockDirty: false);
            MarkDirty(true);
            return true;
        }

        public ItemStack? TakePlate()
        {
            if (!HasPlate) return null;

            ItemStack? stack;
            lock (plateLock)
            {
                stack = PlateStack;
                PlateStack = null;
            }

            ClientPlateChanged(markBlockDirty: false);
            MarkDirty(true);
            return stack;
        }

        public bool TrySetPlate(ItemStack stack)
        {
            if (stack == null) return false;

            lock (plateLock)
            {
                PlateStack = stack;
                PlateStack.StackSize = 1;
            }

            ClientPlateChanged(markBlockDirty: false);
            MarkDirty(true);
            return true;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            try
            {
                ItemStack? loaded = tree.GetItemstack(AttrPlateStack, null);
                loaded?.ResolveBlockOrItem(worldAccessForResolve);

                bool changed;
                lock (plateLock)
                {
                    changed = (PlateStack == null) != (loaded == null)
                        || (PlateStack?.Collectible?.Code != loaded?.Collectible?.Code);
                    PlateStack = loaded;
                }

                if (changed) ClientPlateChanged(markBlockDirty: true);
            }
            catch
            {
                lock (plateLock)
                {
                    PlateStack = null;
                }

                ClientPlateChanged(markBlockDirty: true);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            ItemStack? toSave;
            lock (plateLock)
            {
                toSave = PlateStack;
            }

            if (toSave != null) tree.SetItemstack(AttrPlateStack, toSave);
            else tree.RemoveAttribute(AttrPlateStack);
        }

        partial void ClientPlateChanged(bool markBlockDirty);

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            ItemStack? plate;
            lock (plateLock)
            {
                plate = PlateStack;
            }

            if (plate?.Collectible?.Code != null)
            {
                dsc.AppendLine($"Plate: {plate.Collectible.Code}");
            }
            else
            {
                dsc.AppendLine("Plate: (none)");
            }
        }
    }
}
