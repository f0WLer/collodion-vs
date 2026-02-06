using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Collodion
{
    public sealed partial class BlockEntityDevelopmentTray : BlockEntity
    {
        private const string AttrPlateStack = "collodionPlateStack";

        private readonly object plateLock = new object();
        private string? lastPlateSignature;

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
                lastPlateSignature = ComputePlateSignature(PlateStack);
            }

            ClientPlateChanged(markBlockDirty: true);
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
                lastPlateSignature = null;
            }

            ClientPlateChanged(markBlockDirty: true);
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
                lastPlateSignature = ComputePlateSignature(PlateStack);
            }

            ClientPlateChanged(markBlockDirty: true);
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
                string? newSig = ComputePlateSignature(loaded);
                lock (plateLock)
                {
                    changed = (PlateStack == null) != (loaded == null)
                        || (PlateStack?.Collectible?.Code != loaded?.Collectible?.Code)
                        || !string.Equals(lastPlateSignature, newSig, StringComparison.Ordinal);
                    PlateStack = loaded;
                    lastPlateSignature = newSig;
                }

                if (changed) ClientPlateChanged(markBlockDirty: true);
            }
            catch
            {
                lock (plateLock)
                {
                    PlateStack = null;
                    lastPlateSignature = null;
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

        private static string? ComputePlateSignature(ItemStack? stack)
        {
            if (stack?.Collectible?.Code == null) return null;

            string code = stack.Collectible.Code.ToString();
            string photoId = stack.Attributes?.GetString(WetPlateAttrs.PhotoId) ?? string.Empty;
            string stage = stack.Attributes?.GetString(WetPlateAttrs.PlateStage) ?? string.Empty;
            int pours = 0;
            try
            {
                pours = stack.Attributes?.GetInt(WetPlateAttrs.DevelopPours, 0) ?? 0;
            }
            catch
            {
                pours = 0;
            }

            return $"{code}|{photoId}|{stage}|{pours}";
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
