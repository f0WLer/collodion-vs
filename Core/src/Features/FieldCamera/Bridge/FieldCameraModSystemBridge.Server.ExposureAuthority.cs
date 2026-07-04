using Photocore.CameraCapture;
using Photocore.PhotoMetadata.Model;
using Photocore.PhotoSync;
using Photocore.Plates;
using Photocore.Tray;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Photocore.FieldCamera
{
    // Server-authoritative capture finalization: the bridge's authority over plate-stage transitions
    // as exposures start, pause, finalize, and seal into the tray. Split out of Server.cs so that file
    // stays declarative startup wiring; this is the gameplay authority half of capture.
    internal sealed partial class FieldCameraModSystemBridge
    {
        private void OnExposureStateReceived(IServerPlayer player, ExposureStatePacket packet)
        {
            if (Api?.Side != EnumAppSide.Server || Api.World == null) return;
            if (player == null || packet == null) return;

            BestEffortLogger?.Notification(
                $"photocore[diag]: server recv ExposureState: isExposing={packet.IsExposing} " +
                $"exposedFrames={packet.ExposedFrames} targetFrames={packet.TargetFrames}");

            if (!TryResolveCameraStorage(player, out ItemSlot? cameraSlot, out ItemStack? cameraStack, out BlockEntityMountedCamera? mountedBe)) return;
            if (cameraStack == null || !CameraHasLoadedPlate(cameraStack)) return;

            if (!CameraItemHelper.TryGetLoadedPlateStack(cameraStack, Api.World, out ItemStack? loadedPlate) || loadedPlate == null) return;

            PlateStage stage = PlateAttributes.GetStage(loadedPlate);

            if (packet.IsExposing)
            {
                // Plate must be exposable to transition to Exposing.
                if (stage != PlateStage.Sensitized && stage != PlateStage.ExposurePaused && stage != PlateStage.Exposing) return;

                // A paused exposure can only be resumed by its original photographer — the GPU
                // accumulation buffer lives on that client. Rejecting a foreign resume here also
                // blocks a hijack where a non-owner sends a fresh-start packet (new exposure ID)
                // for someone else's paused plate. Mirrors the mounted-block lock and the client
                // guards in RequestMountedPhotoCapture / CaptureGateService.
                if (stage == PlateStage.ExposurePaused)
                {
                    string? owner = loadedPlate.Attributes.GetString(PlateAttributes.PhotographerUid);
                    if (!string.IsNullOrEmpty(owner)
                        && !string.Equals(owner, player.PlayerUID, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                PlateDryingTransition.TickNow(Api.World, loadedPlate);
                PlateAttributes.SetStage(loadedPlate, PlateStage.Exposing);
                // Stamp photographer + capture date on first exposure start (Sensitized → Exposing
                // only; not a resume from ExposurePaused) — shutter-open is "taken", not seal time.
                if (stage == PlateStage.Sensitized)
                {
                    loadedPlate.Attributes.SetString(PlateAttributes.PhotographerUid, player.PlayerUID);
                    PlateAttributes.SetPhotographerName(loadedPlate, player.PlayerName);
                    PlateAttributes.SetCaptureDate(loadedPlate, Api.World.Calendar);
                }
                if (!string.IsNullOrEmpty(packet.ExposureId))
                    loadedPlate.Attributes.SetString(PlateAttributes.ExposureId, packet.ExposureId);
                if (packet.TargetFrames > 0)
                    loadedPlate.Attributes.SetInt(PlateAttributes.ExposureTargetFrames, packet.TargetFrames);
            }
            else
            {
                if (stage != PlateStage.Exposing && stage != PlateStage.ExposurePaused) return;

                PlateDryingTransition.TickNow(Api.World, loadedPlate);

                // Every eligibility check downstream (CameraEligibility, tray insertion) trusts this
                // stage rather than re-deriving "are we done" itself -- this is the only place that
                // decision is made. Terminal Exposed happens only when the client's hard accumulation
                // cap halted the exposure, the same condition for every camera type; anything else
                // (manual pause, an automatic shutter's timed close) stays resumable. Setting Exposed
                // here does not seal a photo; only developing it in a tray does.
                PlateAttributes.SetStage(loadedPlate, packet.ReachedCap ? PlateStage.Exposed : PlateStage.ExposurePaused);
                loadedPlate.Attributes.SetInt(PlateAttributes.ExposedFrames, packet.ExposedFrames);
                BestEffortLogger?.Notification($"photocore[diag]: server wrote ExposedFrames={packet.ExposedFrames} to plate (reachedCap={packet.ReachedCap})");
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
            if (trayStage != PlateStage.Exposed
                && trayStage != PlateStage.ExposurePaused
                && trayStage != PlateStage.Developing
                && trayStage != PlateStage.Developed
                && trayStage != PlateStage.Finished) return;

            string exposureId = trayPlate.Attributes.GetString(PlateAttributes.ExposureId) ?? string.Empty;
            if (!string.Equals(exposureId, packet.ExposureId, StringComparison.OrdinalIgnoreCase)) return;

            // Develop whitelist: a legitimate client is pre-gated and never reaches here; this denies a
            // modified client. Refuse to register the upload — the existing IsExpected gate then drops it.
            if (!DevelopAllowed(player))
            {
                TellNotWhitelisted(player);
                return;
            }

            trayPlate.Attributes.SetString(PhotographAttrs.PhotoId, photoId);
            trayPlate.Attributes.RemoveAttribute(PlateAttributes.ExposureId);
            trayPlate.Attributes.RemoveAttribute(PlateAttributes.ExposedFrames);
            trayPlate.Attributes.RemoveAttribute(PlateAttributes.ExposureTargetFrames);
            // Only ExposurePaused needs promoting -- rewinding an already-Developing/Developed/
            // Finished plate back to Exposed would erase real tray progress.
            if (trayStage == PlateStage.ExposurePaused)
                PlateAttributes.SetStage(trayPlate, PlateStage.Exposed);
            be.TrySetPlate(trayPlate);

            _owner.PhotoSyncModSystemBridge.ServerTouchPhotoSeen(photoId);
            _owner.PhotoSyncModSystemBridge.Runtime?.RegisterExpectedUpload(player.PlayerUID, photoId);
        }

        // Authoritative develop gate. Open when the whitelist is uninitialised/disabled; operators always pass.
        private bool DevelopAllowed(IServerPlayer player)
            => _owner.AdminToolingBridge.ExposureWhitelist?.IsAllowed(player) ?? true;

        private static void TellNotWhitelisted(IServerPlayer player)
            => player.SendMessage(GlobalConstants.GeneralChatGroup,
                Lang.Get("photocore:msg-develop-not-whitelisted"), EnumChatType.Notification);
    }
}
