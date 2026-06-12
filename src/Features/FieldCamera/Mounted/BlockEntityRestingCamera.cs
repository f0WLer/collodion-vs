using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Collodion.FieldCamera
{
    public sealed class BlockEntityRestingCamera : BlockEntity
    {
        private const string CameraStackAttr = "collodionRestingCameraStack";
        private const string FacingYawAttr   = "collodionRestingCameraYaw";

        private ItemStack? _cameraStack;
        private float _facingYaw;

        internal ItemStack? GetStoredCameraStack(IWorldAccessor? world)
        {
            _cameraStack?.ResolveBlockOrItem(world);
            return _cameraStack;
        }

        internal void SetStoredCameraStack(ItemStack cameraStack, float facingYaw, IWorldAccessor? world)
        {
            _cameraStack = cameraStack.Clone();
            _cameraStack.ResolveBlockOrItem(world);
            _facingYaw = facingYaw;
            MarkDirty(true);
        }

        internal ItemStack? TakeStoredCameraStack(IWorldAccessor? world)
        {
            ItemStack? stored = _cameraStack?.Clone();
            stored?.ResolveBlockOrItem(world);
            _cameraStack = null;
            MarkDirty(true);
            return stored;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (_cameraStack != null)
                tree.SetItemstack(CameraStackAttr, _cameraStack);
            else
                tree.RemoveAttribute(CameraStackAttr);

            tree.SetFloat(FacingYawAttr, _facingYaw);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            _cameraStack = tree.GetItemstack(CameraStackAttr, null);
            _cameraStack?.ResolveBlockOrItem(worldAccessForResolve);
            _facingYaw = tree.GetFloat(FacingYawAttr, 0f);
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            if (_cameraStack?.Collectible?.GetHeldItemName(_cameraStack) is string name && !string.IsNullOrWhiteSpace(name))
                dsc.AppendLine(name);
        }
    }
}
