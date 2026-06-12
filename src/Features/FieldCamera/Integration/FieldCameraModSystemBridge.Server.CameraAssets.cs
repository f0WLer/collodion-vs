using Collodion.Plates;
using Vintagestory.API.Common;

namespace Collodion.FieldCamera
{
    internal sealed partial class FieldCameraModSystemBridge
    {
        private static readonly AssetLocation _sensitizedPlateItemCode = new AssetLocation("collodion", "sensitizedplate");
        private static readonly AssetLocation _fieldcameraBaseCode = new AssetLocation("collodion", "fieldcamera");
        // Asset path remains "loaded-silvered" for backward compatibility; gameplay semantics are sensitized.
        private static readonly AssetLocation _fieldcameraLoadedSensitizedCode = new AssetLocation("collodion", "fieldcamera-loaded-silvered");
        private static readonly AssetLocation _fieldcameraLoadedExposedCode = new AssetLocation("collodion", "fieldcamera-loaded-exposed");
        private static readonly AssetLocation _cameraPlateLoadSound = new AssetLocation("collodion", "sounds/glass-slide1");
        private static readonly AssetLocation _cameraPlateUnloadSound = new AssetLocation("collodion", "sounds/glass-slide2");

        private static AssetLocation GetBaseCode(ItemStack? cameraStack)
            => cameraStack?.Item is ItemFieldcamera cam ? cam.CameraBaseCode : _fieldcameraBaseCode;

        private static AssetLocation GetLoadedCameraCodeForPlate(ItemStack? cameraStack, ItemStack? loadedPlate)
        {
            PlateStage stage = PlateAttributes.GetStage(loadedPlate);
            bool exposed = stage == PlateStage.Exposing
                            || stage == PlateStage.ExposurePaused;
            return cameraStack?.Item is ItemFieldcamera cam
                ? (exposed ? cam.CameraLoadedExposedCode : cam.CameraLoadedSensitizedCode)
                : (exposed ? _fieldcameraLoadedExposedCode : _fieldcameraLoadedSensitizedCode);
        }

        private ItemStack? ReplaceCameraCode(ItemStack? cameraStack, AssetLocation code)
        {
            if (Api == null || cameraStack == null) return cameraStack;
            if (cameraStack.Collectible?.Code == code) return cameraStack;

            Item? item = Api.World.GetItem(code);
            if (item == null) return cameraStack;

            ItemStack replacement = new ItemStack(item, cameraStack.StackSize);
            replacement.Attributes.MergeTree(cameraStack.Attributes.Clone());
            return replacement;
        }

        private void SetCameraCode(ItemSlot cameraSlot, AssetLocation code)
        {
            ItemStack? replacement = ReplaceCameraCode(cameraSlot.Itemstack, code);
            if (replacement == null) return;
            cameraSlot.Itemstack = replacement;
            cameraSlot.MarkDirty();
        }
    }
}