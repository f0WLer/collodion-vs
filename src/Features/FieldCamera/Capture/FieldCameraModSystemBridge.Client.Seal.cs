using Collodion.AdminTooling;
using Collodion.CameraCapture.Contracts;
using Collodion.Exposure;
using Collodion.ImageEffects;
using Collodion.PhotoSync.Integration;
using Collodion.Plates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion.FieldCamera
{
    // Split out of the Client wiring partial because this is exposure-finalization logic, not bootstrap.
    internal sealed partial class FieldCameraModSystemBridge
    {
        internal bool TrySendSealForTray(ICoreClientAPI capi, BlockPos trayPos, ItemStack trayPlate)
        {
            if (ClientChannel == null) return false;

            string exposureId = trayPlate.Attributes?.GetString(PlateAttributes.ExposureId) ?? string.Empty;
            if (string.IsNullOrEmpty(exposureId)) return false;

            PlateProcessProfile profile = PlateProcessProfile.Iodide;

            // Tray development must match normal export policy: same target exposure, output size, and effects resolution.
            int targetFrames = Math.Max(1, trayPlate.Attributes?.GetInt(PlateAttributes.ExposureTargetFrames) ?? profile.SampleCount);
            int maxDimension = CollodionConfigAccess.ResolveClientConfig(capi)?.Viewfinder?.PhotoCaptureMaxDimension
                ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;
            ImageEffectsConfig baselineEffects = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
            ImageEffectsConfig? effectsOverride = ImageEffectsProfileService.TryLoadProfile("wetplate", capi);

            string? photoId = PartialExposureSealer.SealToPng(
                exposureId,
                capi,
                profile,
                targetFrames,
                maxDimension,
                baselineEffects,
                effectsOverride);
            if (string.IsNullOrEmpty(photoId)) return false;

            ExposureAccumulationStore.Delete(exposureId);
            ClientPhotoSyncIntegration.NotifyPhotoCreated(capi, photoId);

            ClientChannel.SendPacket(new SealAndInsertIntoTrayPacket
            {
                ExposureId = exposureId,
                PhotoId    = photoId,
                TrayPosX   = trayPos.X,
                TrayPosY   = trayPos.Y,
                TrayPosZ   = trayPos.Z,
                TrayPosDim = trayPos.dimension,
            });
            return true;
        }
    }
}
