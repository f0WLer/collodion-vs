using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    public sealed class ItemFinishedPhotoPlate : ItemPlateBase
    {
        public static int ClearClientRenderCacheAndBumpVersion()
        {
            return PhotoPlateRenderUtil.ClearClientRenderCacheAndBumpVersion();
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            PhotoPlateRenderUtil.TryRenderPhotoOverlay(capi, itemstack, target, ref renderinfo);
        }
    }
}
