using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Photocore.PlateBox
{
    internal static class PlateBoxRenderLifecycle
    {
        internal static PlateBoxSlotRenderer? EnsureRendererRegistered(ICoreAPI api, BlockEntityPlateBox owner, PlateBoxSlotRenderer? renderer)
        {
            if (api?.Side != EnumAppSide.Client) return renderer;
            if (renderer != null) return renderer;

            ICoreClientAPI capi = (ICoreClientAPI)api;
            renderer = new PlateBoxSlotRenderer(capi, owner);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "photocore-platebox-slotrender");
            return renderer;
        }

        internal static void TryMarkBlockDirty(ICoreAPI? api, BlockPos pos)
        {
            if (api?.Side != EnumAppSide.Client) return;

            ((ICoreClientAPI)api).World.BlockAccessor.MarkBlockDirty(pos);
        }

        internal static PlateBoxSlotRenderer? DisposeRenderer(ICoreAPI? api, PlateBoxSlotRenderer? renderer)
        {
            if (api?.Side != EnumAppSide.Client || renderer == null) return renderer;

            try
            {
                ICoreClientAPI capi = (ICoreClientAPI)api;
                capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
                renderer.Dispose();
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }
}