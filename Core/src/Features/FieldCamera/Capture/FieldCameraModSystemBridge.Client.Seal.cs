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

            EmulsionProfile profile = EmulsionProfile.Resolve(PlateAttributes.GetChemistry(trayPlate));

            // Develop with the plate's own SAVED chemistry profile (config), never the live dialog values —
            // the dialog only drives the preview window; its values reach develop only once saved. Canonical
            // physics flags + this chemistry's saved exposure params.
            ExposurePhysicsConfig physics = new() { Chem = ChemistryProfileRegistry.Instance.Get(profile.Name).ExposurePhysics };

            // Tray development must match normal export policy: same target exposure, output size, and effects resolution.
            ViewfinderConfig? viewfinder = PhotochemistryConfigAccess.ResolveClientConfig(capi)?.Viewfinder;
            int targetFrames = Math.Max(1, trayPlate.Attributes?.GetInt(PlateAttributes.ExposureTargetFrames) ?? profile.SampleCount);
            int maxDimension = viewfinder?.PhotoCaptureMaxDimension ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;
            // Config gate for finishing on the saved plate photo — independent of the dialog's preview toggle.
            bool applyFinishing = viewfinder?.ApplyFinishingEffects ?? true;
            // Finishing effects come from this chemistry's profile, matching what the preview showed.
            ImageEffectsConfig effects = ChemistryProfileRegistry.Instance.Get(profile.Name).PostEffects;

            string? photoId = PartialExposureSealer.SealToPng(
                exposureId,
                capi,
                profile,
                physics,
                targetFrames,
                maxDimension,
                effects,
                applyFinishing);
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
