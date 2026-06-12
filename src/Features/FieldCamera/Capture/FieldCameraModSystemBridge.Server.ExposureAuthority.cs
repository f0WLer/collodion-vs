using Collodion.CameraCapture.Contracts;
using Collodion.PhotoMetadata.Model;
using Collodion.PhotoSync.Storage;
using Collodion.Plates;
using Collodion.Tray;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion.FieldCamera
{
    // Server-authoritative capture finalization: the bridge's authority over plate-stage transitions
    // as exposures start, pause, finalize, and seal into the tray. Split out of Server.cs so that file
    // stays declarative startup wiring; this is the gameplay authority half of capture.
    internal sealed partial class FieldCameraModSystemBridge
    {
        private void OnPhotoTakenReceived(IServerPlayer player, PhotoTakenPacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null) return;
            if (player == null || packet == null) return;

            string photoId = PhotoAssetStoragePaths.NormalizePhotoId(packet.PhotoId);
            if (string.IsNullOrWhiteSpace(photoId)) return;

            if (!TryResolveCameraStorage(player, out ItemSlot? cameraSlot, out ItemStack? cameraStack, out BlockEntityMountedCamera? mountedBe)) return;
            if (cameraStack == null || !CameraHasLoadedPlate(cameraStack)) return;

            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null) return;

            PlateStage stage = PlateAttributes.GetStage(loadedPlate);
            // Exposed or later means the photo was already recorded — nothing to finalize.
            if (stage != PlateStage.Sensitized && stage != PlateStage.Exposing && stage != PlateStage.ExposurePaused) return;

            // Plate expired before the shutter fired — drying clock ran out mid-exposure.
            if (PlateDryingTransition.IsDry(Api.World, loadedPlate)) return;

            PlateAttributes.SetStage(loadedPlate, PlateStage.Exposed);
            loadedPlate.Attributes.SetString(PhotographAttrs.PhotoId, photoId);
            loadedPlate.Attributes.RemoveAttribute(PlateAttributes.ExposureId);
            loadedPlate.Attributes.RemoveAttribute(PlateAttributes.ExposedFrames);
            loadedPlate.Attributes.RemoveAttribute(PlateAttributes.ExposureTargetFrames);
            loadedPlate.Attributes.RemoveAttribute(PlateAttributes.PhotographerUid);
            CameraItemHelper.ClearMountedCaptureState(cameraStack);

            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            if (mountedBe != null)
            {
                ItemStack? updatedCamera = ReplaceCameraCode(cameraStack, GetLoadedCameraCodeForPlate(cameraStack, loadedPlate));
                if (updatedCamera == null) return;
                mountedBe.SetStoredCameraStack(updatedCamera, mountedBe.OwnerPlayerUid, Api.World);
            }
            else if (cameraSlot != null)
            {
                SetCameraCode(cameraSlot, GetLoadedCameraCodeForPlate(cameraStack, loadedPlate));
                cameraSlot.MarkDirty();
            }

            _owner.PhotoSyncModSystemBridge.ServerTouchPhotoSeen(photoId);

            // Authorize the matching upload so the client's chunk packets are not rejected as unsolicited.
            _owner.PhotoSyncModSystemBridge.Runtime?.RegisterExpectedUpload(player.PlayerUID, photoId);
        }

        private void OnExposureStateReceived(IServerPlayer player, ExposureStatePacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null) return;
            if (player == null || packet == null) return;

            if (!TryResolveCameraStorage(player, out ItemSlot? cameraSlot, out ItemStack? cameraStack, out BlockEntityMountedCamera? mountedBe)) return;
            if (cameraStack == null || !CameraHasLoadedPlate(cameraStack)) return;

            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null) return;

            PlateStage stage = PlateAttributes.GetStage(loadedPlate);

            if (packet.IsExposing)
            {
                // Plate must be exposable to transition to Exposing.
                if (stage != PlateStage.Sensitized && stage != PlateStage.ExposurePaused && stage != PlateStage.Exposing) return;

                PlateDryingTransition.TickNow(Api.World, loadedPlate);
                PlateAttributes.SetStage(loadedPlate, PlateStage.Exposing);
                // Stamp photographer on first exposure start (Sensitized → Exposing only; not a resume from ExposurePaused).
                if (stage == PlateStage.Sensitized)
                    loadedPlate.Attributes.SetString(PlateAttributes.PhotographerUid, player.PlayerUID);
                if (!string.IsNullOrEmpty(packet.ExposureId))
                    loadedPlate.Attributes.SetString(PlateAttributes.ExposureId, packet.ExposureId);
                if (packet.TargetFrames > 0)
                    loadedPlate.Attributes.SetInt(PlateAttributes.ExposureTargetFrames, packet.TargetFrames);
            }
            else
            {
                // Pausing: accept only from Exposing.
                if (stage != PlateStage.Exposing && stage != PlateStage.ExposurePaused) return;

                PlateDryingTransition.TickNow(Api.World, loadedPlate);
                PlateAttributes.SetStage(loadedPlate, PlateStage.ExposurePaused);
                loadedPlate.Attributes.SetInt(PlateAttributes.ExposedFrames, packet.ExposedFrames);
            }

            SetLoadedPlateAttributes(cameraStack, loadedPlate);
            if (mountedBe != null)
                mountedBe.MarkCameraDirty();
            else
                cameraSlot?.MarkDirty();
        }

        // When the developer pour has not yet advanced, transitions ExposurePaused → Exposed so the
        // pending pour sees the correct stage. Later arrivals keep the current stage intact.
        private void OnSealAndInsertTrayReceived(IServerPlayer player, SealAndInsertIntoTrayPacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null) return;
            if (player == null || packet == null) return;

            string photoId = PhotoAssetStoragePaths.NormalizePhotoId(packet.PhotoId);
            if (string.IsNullOrWhiteSpace(photoId)) return;

            BlockPos trayPos = new BlockPos(packet.TrayPosX, packet.TrayPosY, packet.TrayPosZ, packet.TrayPosDim);
            if (Api.World.BlockAccessor.GetBlockEntity(trayPos) is not BlockEntityDevelopmentTray be) return;

            ItemStack? trayPlate = be.PlateStack;
            if (trayPlate == null) return;

            PlateStage trayStage = PlateAttributes.GetStage(trayPlate);
            if (trayStage != PlateStage.ExposurePaused
                && trayStage != PlateStage.Developing
                && trayStage != PlateStage.Developed
                && trayStage != PlateStage.Finished) return;

            string exposureId = trayPlate.Attributes.GetString(PlateAttributes.ExposureId) ?? string.Empty;
            if (!string.Equals(exposureId, packet.ExposureId, StringComparison.OrdinalIgnoreCase)) return;

            trayPlate.Attributes.SetString(PhotographAttrs.PhotoId, photoId);
            trayPlate.Attributes.RemoveAttribute(PlateAttributes.ExposureId);
            trayPlate.Attributes.RemoveAttribute(PlateAttributes.ExposedFrames);
            trayPlate.Attributes.RemoveAttribute(PlateAttributes.ExposureTargetFrames);
            // Only change stage for ExposurePaused; if the tray already advanced to Developing/Developed/Finished,
            // just set the photoId and clean up exposure attrs without rewinding the stage.
            if (trayStage == PlateStage.ExposurePaused)
                PlateAttributes.SetStage(trayPlate, PlateStage.Exposed);
            be.TrySetPlate(trayPlate);

            _owner.PhotoSyncModSystemBridge.ServerTouchPhotoSeen(photoId);
            _owner.PhotoSyncModSystemBridge.Runtime?.RegisterExpectedUpload(player.PlayerUID, photoId);
        }
    }
}
