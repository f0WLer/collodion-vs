using Collodion.AdminTooling;
using Collodion.CameraCapture;
using Collodion.CameraCapture.Contracts;
using Collodion.Exposure;
using Collodion.ImageEffects;
using Collodion.PhotoSync.Integration;
using Collodion.Plates;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Config;

namespace Collodion.FieldCamera
{
    internal sealed partial class FieldCameraClientRuntime
    {
        private string _mountedExposureId = string.Empty;
        private VirtualCameraState? _pendingMountedCameraState;
        private long _lastShutterGateChatMs;
        private int _maxFrames = ViewfinderConfig.DefaultMaxAccumulatedFrames;

        internal bool TryToggleViewfinderExposure(EntityAgent byEntity, bool silentIfBusy)
        {
            var acc = _owner.Capture.ActiveAccumulator;

            if (acc?.IsCapturing == true)
            {
                acc.Pause();

                if (acc.FramesAccumulated >= acc.TargetFrames)
                {
                    ExportAndSealExposure(byEntity);
                }
                else
                {
                    if (acc is ViewportExposureAccumulator viewportAcc
                        && acc.FramesAccumulated > 0
                        && !string.IsNullOrEmpty(_owner.Capture.ActiveExposureId))
                    {
                        byte[]? blob = viewportAcc.ExportPartial();
                        if (blob != null && !ExposureAccumulationStore.Save(_owner.Capture.ActiveExposureId, blob))
                            _owner.ClientApi?.ShowChatMessage(Lang.Get("photochemistry:msg-exposure-save-failed"));
                    }

                    SendExposureStatePacket(isExposing: false, acc.FramesAccumulated, _owner.Capture.ActiveExposureId, acc.TargetFrames);
                }

                // Exit viewfinder immediately when pausing if RMB is not held.
                if (!GetRightMouseDown() && _owner.Capture.IsViewfinderActive)
                    _owner.Capture.EndViewfinderMode();

                return true;
            }

            if (!FieldCameraModSystemBridge.CaptureGateService.TryValidateCaptureRequest(_owner, silentIfBusy, isMounted: false, out ItemStack? loadedPlateStack)) return false;

            var clientApi = _owner.ClientApi;
            if (clientApi == null) return false;

            string exposureId = loadedPlateStack?.Attributes?.GetString(PlateAttributes.ExposureId) ?? string.Empty;

            if (!string.IsNullOrEmpty(exposureId) && ViewfinderExposureRegistry.TryGet(exposureId, out var existingAcc) && existingAcc != null
                && existingAcc.State == ExposureState.Paused)
            {
                if (existingAcc.FramesAccumulated >= _maxFrames)
                {
                    if (!silentIfBusy)
                        ShowShutterGateMessageThrottled(Lang.Get("photochemistry:shuttergate-maxframes-message", _maxFrames));
                    return false;
                }
                _owner.Capture.ActiveAccumulator = existingAcc;
                _owner.Capture.ActiveExposureId = exposureId;
                existingAcc.Resume();
                SendExposureStatePacket(isExposing: true, existingAcc.FramesAccumulated, exposureId, existingAcc.TargetFrames);
                return true;
            }

            // Fresh exposure: generate a new session ID and allocate a new accumulator.
            // Exception: if the plate already carries an exposure ID with a saved partial blob
            // (e.g. transferred mid-exposure from a mounted camera), keep that ID so the
            // accumulated frames are restored and the plate's ID doesn't get orphaned.
            byte[]? crossCameraBlob = null;
            if (!string.IsNullOrEmpty(exposureId) &&
                ExposureAccumulationStore.TryLoad(exposureId, out byte[]? storedBlob) &&
                storedBlob != null)
            {
                crossCameraBlob = storedBlob;
                // Keep exposureId — do NOT generate a new one.
            }
            else
            {
                exposureId = Guid.NewGuid().ToString("N");
            }

            PlateProcessProfile profile = PlateProcessProfile.Iodide;

            // When Prime() was called, the PBO ring is already warm so the first sample tick maps
            // a real frame immediately — no sync GL.ReadPixels stall, no 2-kick priming gap.
            ViewportExposureAccumulator newAcc = _owner.Capture._primedViewportAccumulator ?? new ViewportExposureAccumulator(clientApi);
            _owner.Capture._primedViewportAccumulator = null;
            newAcc.ExposurePreviewSink = _owner.Capture._virtualCameraPreviewRenderer;
            newAcc.OnAutoHalt = () => OnAccumulatorAutoHalt(byEntity, newAcc, exposureId);
            newAcc.Start(profile);

            if (crossCameraBlob != null)
                newAcc.PrimeFromPartial(crossCameraBlob);

            ViewfinderExposureRegistry.Register(exposureId, newAcc);
            _owner.Capture.ActiveAccumulator = newAcc;
            _owner.Capture.ActiveExposureId = exposureId;

            _owner.Capture.MaybeShowF4GuiLessTip();
            SendExposureStatePacket(isExposing: true, newAcc.FramesAccumulated, exposureId, newAcc.TargetFrames);

            return true;
        }

        // Called by the accumulator's auto-halt callback once target frames are reached.
        private void OnAccumulatorAutoHalt(EntityAgent byEntity, ViewportExposureAccumulator acc, string exposureId)
        {
            // Auto-halt exits the viewfinder and seals the exposure.
            _suppressViewfinderUntilRmbReleased = true;
            ExportAndSealExposure(byEntity, exposureId);
            if (_owner.Capture.IsViewfinderActive) _owner.Capture.EndViewfinderMode();
        }

        private void ExportAndSealExposure(EntityAgent? byEntity, string? knownExposureId = null)
        {
            var acc = _owner.Capture.ActiveAccumulator;
            if (acc == null || acc.FramesAccumulated == 0)
            {
                _owner.Capture.ActiveAccumulator = null;
                return;
            }

            try
            {
                var clientApi = _owner.ClientApi;
                if (clientApi == null) return;

                ItemStack? camStack = CameraItemHelper.GetActiveCameraStack(clientApi);
                CameraItemHelper.TryGetLoadedPlateStack(camStack, clientApi.World, out ItemStack? loadedPlate);
                ImageEffectsConfig? effectsOverride = ImageEffectsProfileService.TryLoadProfile("wetplate", clientApi);

                acc.Stop();
                string fileName = acc.Export(effectsOverride);

                _owner.ClientChannel?.SendPacket(new PhotoTakenPacket { PhotoId = fileName });
                ClientPhotoSyncIntegration.NotifyPhotoCreated(clientApi, fileName);

                if (byEntity != null)
                    clientApi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/woodclick"), byEntity, null, true, 32, 1f);

                string exposureId = knownExposureId
                    ?? _owner.Capture.ActiveExposureId
                    ?? loadedPlate?.Attributes?.GetString(PlateAttributes.ExposureId)
                    ?? string.Empty;
                if (!string.IsNullOrEmpty(exposureId))
                {
                    ExposureAccumulationStore.Delete(exposureId);
                    ViewfinderExposureRegistry.Remove(exposureId);
                }
            }
            catch (Exception ex)
            {
                _owner.ClientApi?.Logger.Error("photochemistry: accumulation export failed — " + ex);
            }
            finally
            {
                _owner.Capture.ActiveAccumulator = null;
                _owner.Capture.ActiveExposureId = string.Empty;
            }
        }

        private void SendExposureStatePacket(bool isExposing, int exposedFrames, string exposureId, int targetFrames)
        {
            _owner.ClientChannel?.SendPacket(new ExposureStatePacket
            {
                IsExposing = isExposing,
                ExposureId = exposureId,
                ExposedFrames = exposedFrames,
                TargetFrames = targetFrames
            });
        }

        internal void ShowShutterGateMessageThrottled(string message)
        {
            if (_owner.ClientApi == null) return;

            long nowMs = Environment.TickCount64;
            if (nowMs - _lastShutterGateChatMs <= 1000) return;

            _lastShutterGateChatMs = nowMs;
            _owner.ClientApi.ShowChatMessage(message);
        }

        // No plate is required at placement — the exposure doesn't start until the player right-clicks the block.
        internal bool TryToggleMountedExposure(bool silentIfBusy)
        {
            var renderer = _owner.Capture._virtualExposureRenderer;
            if (renderer == null) return false;

            // Guard: don't overwrite a completed but not-yet-exported session.
            if (renderer.State == ExposureState.Done) return true;

            var clientApi = _owner.ClientApi;
            if (clientApi == null) return false;

            // Snapshot the player's eye position so the block can restart the renderer later,
            // even after the client relaunches and the renderer instance is gone.
            var player = clientApi.World.Player;
            var sidedPos = player.Entity.Pos;
            _pendingMountedCameraState = new VirtualCameraState(
                sidedPos.XYZ.AddCopy(0, player.Entity.LocalEyePos.Y, 0),
                sidedPos.Yaw,
                sidedPos.Pitch,
                ((ClientMain)clientApi.World).MainCamera.Fov,
                sidedPos.Dimension,
                selfPortrait: true);

            // Immediately prepare the virtual camera for idle preview so the preview window
            // updates without waiting for the server round-trip. The server will later confirm
            // and resend PrepareIdlePreview=true, which is a harmless no-op at that point.
            renderer.PrepareCamera(_pendingMountedCameraState.Value);

            _owner.ClientChannel?.SendPacket(CreateCameraMountRequest(_pendingMountedCameraState.Value));
            return true;
        }

        internal void ApplyMountedExposureControl(MountedCameraControlPacket packet)
        {
            var renderer = _owner.Capture._virtualExposureRenderer;
            if (renderer == null || packet == null) return;

            // Track which camera block this player is shooting through so its renderer hides only
            // that camera from the virtual capture. Cleared when the server reports no mount block
            // (i.e. the player has dismounted).
            ViewportExposureSuppressContext.ActiveMountedCameraPos = packet.HasMountBlock
                ? new BlockPos(packet.MountBlockX, packet.MountBlockY, packet.MountBlockZ)
                : null;

            if (!string.IsNullOrEmpty(packet.ExposureId))
                _mountedExposureId = packet.ExposureId;
            else if (string.IsNullOrEmpty(_mountedExposureId) || packet.IsExposing)
                // Either no ID at all (first mount), or a fresh-start packet arrived with no ID from
                // the server (defensive: prevents inheriting a stale ID from a previous camera session).
                _mountedExposureId = Guid.NewGuid().ToString("N");

            if (packet.HasCameraState)
            {
                _pendingMountedCameraState = new VirtualCameraState(
                    new Vec3d(packet.CameraPosX, packet.CameraPosY, packet.CameraPosZ),
                    packet.CameraYaw,
                    packet.CameraPitch,
                    packet.CameraFov,
                    packet.CameraDimension,
                    selfPortrait: true);

                var clientApi = _owner.ClientApi;
                if (clientApi != null)
                {
                    // Mounted-plate chemistry isn't threaded to the client here yet, so this
                    // resolves to the collodion/iodide default. Pass the loaded plate once available.
                    PlateProcessProfile previewProcess = PlateProcessProfile.Iodide;

                    // Keep idle and live mounted preview chemistry aligned with the active plate.
                    if (_owner.Capture._virtualCameraPreviewRenderer != null)
                        _owner.Capture._virtualCameraPreviewRenderer.EmulsionProcess = previewProcess;
                }
            }

            if (packet.PrepareIdlePreview)
            {
                if (_pendingMountedCameraState is VirtualCameraState idleCameraState)
                    renderer.PrepareCamera(idleCameraState);
            }
            else if (!packet.IsExposing)
            {
                renderer.ClearCamera();
            }

            if (packet.IsExposing)
            {
                if (renderer.State == ExposureState.Capturing)
                {
                    ShowShutterGateMessageThrottled(Lang.Get("photochemistry:msg-exposure-already-active"));
                    return;
                }

                if (_pendingMountedCameraState is VirtualCameraState cameraState)
                {
                    var clientApi = _owner.ClientApi;
                    if (clientApi == null) return;

                    // Mounted-plate chemistry isn't threaded to the client here yet, so this
                    // resolves to the collodion/iodide default. Pass the loaded plate once available.
                    PlateProcessProfile profile = PlateProcessProfile.Iodide;

                    renderer.ApplyFinishing = false;
                    renderer.ExposurePreviewSink = _owner.Capture._virtualCameraPreviewRenderer;
                    renderer.Start(cameraState, profile);
                    _maxFrames = CollodionConfigAccess.ResolveClientConfig(clientApi)?.Viewfinder?.MaxAccumulatedFrames
                        ?? ViewfinderConfig.DefaultMaxAccumulatedFrames;

                    if (!string.IsNullOrEmpty(_mountedExposureId) &&
                        ExposureAccumulationStore.TryLoad(_mountedExposureId, out byte[]? partialData) &&
                        partialData != null)
                    {
                        renderer.PrimeFromPartial(partialData);

                        // After restoring the partial, enforce the hard frame limit so the player
                        // cannot bypass it by repeatedly resuming after auto-stop (each attempt
                        // would otherwise accumulate one extra frame before stopping again).
                        if (renderer.FramesAccumulated >= _maxFrames)
                        {
                            ShowShutterGateMessageThrottled(Lang.Get("photochemistry:msg-plate-max-frames"));
                            renderer.Discard();
                            return;
                        }
                    }

                    SendExposureStatePacket(true, renderer.FramesAccumulated, _mountedExposureId, renderer.CapFrameCount);
                }
                return;
            }

            if (renderer.State != ExposureState.Capturing) return;

            renderer.Pause();

            int framesAtPause = renderer.FramesAccumulated;
            if (framesAtPause > 0 && !string.IsNullOrEmpty(_mountedExposureId))
            {
                byte[]? blob = renderer.ExportPartial();
                if (blob != null && !ExposureAccumulationStore.Save(_mountedExposureId, blob))
                    _owner.ClientApi?.ShowChatMessage(Lang.Get("photochemistry:msg-exposure-save-failed"));
            }

            renderer.Discard();

            SendExposureStatePacket(false, framesAtPause, _mountedExposureId, renderer.CapFrameCount);
        }

        private void PersistPartialMountedExposure()
        {
            var renderer = _owner.Capture._virtualExposureRenderer;
            if (renderer == null) return;

            if (renderer.FramesAccumulated > 0 && !string.IsNullOrEmpty(_mountedExposureId))
            {
                byte[]? blob = renderer.ExportPartial();
                if (blob != null && !ExposureAccumulationStore.Save(_mountedExposureId, blob))
                    _owner.ClientApi?.ShowChatMessage(Lang.Get("photochemistry:msg-exposure-save-failed"));
            }

            // Tell the server to set the plate to ExposurePaused with the current frame count.
            SendExposureStatePacket(false, renderer.FramesAccumulated, _mountedExposureId, renderer.CapFrameCount);

            renderer.Discard();
            // Intentionally keep _mountedExposureId and _pendingMountedCameraState so the
            // player can right-click the block to resume without dismounting and remounting.
        }

        private static CameraMountRequestPacket CreateCameraMountRequest(in VirtualCameraState cameraState)
        {
            return new CameraMountRequestPacket
            {
                CameraPosX = cameraState.Position.X,
                CameraPosY = cameraState.Position.Y,
                CameraPosZ = cameraState.Position.Z,
                CameraYaw = cameraState.Yaw,
                CameraPitch = cameraState.Pitch,
                CameraFov = cameraState.Fov,
                CameraDimension = cameraState.Dimension,
            };
        }
    }
}
