using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Collodion
{
    public sealed class BlockEntityWallMountedCameraSling : BlockEntity
    {
        private const string SlingStackAttr = "collodionSlingStack";

        public ItemStack? SlingStack { get; private set; }

        public void SetSlingStack(ItemStack stack)
        {
            SlingStack = stack?.Clone();
            SlingStack?.ResolveBlockOrItem(Api?.World);
            MarkDirty(true);

            try
            {
                Api?.World?.BlockAccessor?.MarkBlockEntityDirty(Pos);
            }
            catch
            {
                // ignore
            }
        }

        public ItemStack? TakeSlingStack()
        {
            ItemStack? stack = SlingStack?.Clone();
            SlingStack = null;
            MarkDirty(true);

            try
            {
                Api?.World?.BlockAccessor?.MarkBlockEntityDirty(Pos);
            }
            catch
            {
                // ignore
            }

            return stack;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (SlingStack != null)
            {
                tree.SetItemstack(SlingStackAttr, SlingStack);
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            SlingStack = tree.GetItemstack(SlingStackAttr);
            SlingStack?.ResolveBlockOrItem(worldAccessForResolve);
        }
    }
}
