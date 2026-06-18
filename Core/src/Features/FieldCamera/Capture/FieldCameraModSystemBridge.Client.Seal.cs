using Photochemistry.AdminTooling;
using Photochemistry.CameraCapture.Contracts;
using Photochemistry.Exposure;
using Photochemistry.ImageEffects;
using Photochemistry.PhotoSync.Integration;
using Photochemistry.Plates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Photochemistry.FieldCamera
{
    // Split out of the Client wiring partial because this is exposure-finalization logic, not bootstrap.
    internal sealed partial class FieldCameraModSystemBridge
    {
        internal bool TrySendSealForTray(ICoreClientAPI capi, BlockPos trayPos, ItemStack trayPlate)
        {
            if (ClientChannel == null) return false;

            string exposureId = trayPlate.Attributes?.GetString(PlateAttributes.ExposureId) ?? string.Empty;
            if (string.IsNullOrEmpty(exposureId)) return false;

            PlateProcessProfile profile = PlateProcessProfile.Resolve(PlateAttributes.GetChemistry(trayPlate));

            // Develop with the live session's tuned physics/chemistry (from the exposure-physics dialog)
            // so the finished photo matches what the prediction preview showed. Falls back to defaults
            // (= raw process profile) when the renderer is unavailable, e.g. after a relog.
            ExposurePhysicsConfig physics = Capture._virtualExposureRenderer?.Physics ?? new ExposurePhysicsConfig();

            // Tray development must match normal export policy: same target exposure, output size, and effects resolution.
            int targetFrames = Math.Max(1, trayPlate.Attributes?.GetInt(PlateAttributes.ExposureTargetFrames) ?? profile.SampleCount);
            int maxDimension = PhotochemistryConfigAccess.ResolveClientConfig(capi)?.Viewfinder?.PhotoCaptureMaxDimension
                ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;
            ImageEffectsConfig baselineEffects = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
            ImageEffectsConfig? effectsOverride = ImageEffectsProfileService.TryLoadProfile("wetplate", capi);

            string? photoId = PartialExposureSealer.SealToPng(
                exposureId,
                capi,
                profile,
                physics,
                targetFrames,
                maxDimension,
                baselineEffects,
                effectsOverride);
            if (string.IsNullOrEmpty(photoId)) return false;

            ClientChannel.SendPacket(new SealAndInsertIntoTrayPacket
            {
                ExposureId = exposureId,
                PhotoId    = photoId,
                TrayPosX   = trayPos.X,
                TrayPosY   = trayPos.Y,
                TrayPosZ   = trayPos.Z,
                TrayPosDim = trayPos.dimension,
            });
            ExposureAccumulationStore.Delete(exposureId);
            ClientPhotoSyncIntegration.NotifyPhotoCreated(capi, photoId);
            return true;
        }
    }
}
