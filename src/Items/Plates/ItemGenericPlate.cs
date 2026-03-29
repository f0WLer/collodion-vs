using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    public class ItemGenericPlate : ItemPlateBase
    {
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            if (PhotoPlateRenderUtil.ShouldRenderPhotoOverlay(itemstack))
            {
                PhotoPlateRenderUtil.TryRenderPhotoOverlay(capi, itemstack, target, ref renderinfo);
            }
        }
    }
}
