using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Photochemistry.AdminTooling;
using Photochemistry.Configuration;

namespace Photochemistry.FieldCamera
{
    public partial class ItemFieldcamera
    {
        private static bool IsViewfinderActive(ICoreAPI api)
        {
            PhotochemistryModSystem? modSys = PhotochemistryConfigAccess.ResolveModSystem(api);
            return modSys != null && modSys.CameraCaptureBridge.IsViewfinderActive;
        }

        private static bool TryStartCapture(ICoreAPI api, EntityAgent byEntity, bool silentIfBusy)
        {
            if (api is not ICoreClientAPI capi) return false;

            PhotochemistryModSystem? modSys = PhotochemistryConfigAccess.ResolveModSystem(api);
            if (modSys == null) return false;

            ItemStack? cameraStack = CameraItemHelper.GetActiveCameraStack(capi);

            if (CameraItemHelper.HasMountedTripod(cameraStack))
                return modSys.FieldCameraBridge.RequestMountedPhotoCapture(silentIfBusy);

            return modSys.FieldCameraBridge.TryToggleViewfinderExposure(byEntity, silentIfBusy);
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (api.Side != EnumAppSide.Client) return false;

            if (!IsViewfinderActive(api))
                return false;

            bool leftDown = byEntity.Controls?.LeftMouseDown == true;
            bool leftPrev = GetLmbPrev(byEntity);
            bool leftPressed = leftDown && !leftPrev;

            SetLmbPrev(byEntity, leftDown);

            if (leftPressed)
                TryStartCapture(api, byEntity, silentIfBusy: true);

            // Keep the interact chain alive while RMB viewfinder is active.
            return true;
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            if (api.Side == EnumAppSide.Client)
                SetLmbPrev(byEntity, false);

            return base.OnHeldInteractCancel(secondsUsed, slot, byEntity, blockSel, entitySel, cancelReason);
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
            if (api.Side == EnumAppSide.Client)
                SetLmbPrev(byEntity, false);
        }

        private const string LmbPrevAttrKey = "photochemCameraLmbPrev";
        private static bool GetLmbPrev(EntityAgent byEntity) => byEntity?.Attributes?.GetBool(LmbPrevAttrKey) ?? false;
        private static void SetLmbPrev(EntityAgent byEntity, bool value) => byEntity?.Attributes?.SetBool(LmbPrevAttrKey, value);
    }
}

