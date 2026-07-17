using Photocore.CameraCapture;
using Photocore.Exposure;
using Photocore.Plates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Config;
using Photocore.Configuration;

namespace Photocore.FieldCamera
{
    internal sealed partial class FieldCameraClientRuntime
    {
        private string _mountedExposureId = string.Empty;
        private VirtualCameraState? _pendingMountedCameraState;
        private long _lastShutterGateChatMs;
        private int _maxFrames = ViewfinderConfig.DefaultMaxAccumulatedFrames;

        // Chains background partial-exposure saves per exposureId so a rapid pause/resume/pause can't
        // let an older, less-progressed write finish after a newer one and clobber it on disk.
        private readonly Dictionary<string, Task> _pendingExposureSaves = new(StringComparer.Ordinal);
        private readonly object _pendingExposureSavesLock = new();

        // Temporary startup-race diagnostics (gated on ShowDebugLogs). See the exposure-flakiness plan.
        private void Diag(string msg) => _owner.BestEffortLogger?.Notification("photocore[diag]: " + msg);

        internal bool TryToggleViewfinderExposure(EntityAgent byEntity, bool silentIfBusy)
        {
            var acc = _owner.Capture.ActiveAccumulator;

            if (acc?.IsCapturing == true)
            {
                acc.Pause();

                // A manual pause never reaches the hard cap by definition -- only developing a
                // plate in a tray creates a photo, regardless of how the exposure stopped.
                HandleExposurePaused(acc, _owner.Capture.ActiveExposureId, reachedCap: false);

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
                // Re-resolve the shutter policy for the camera being resumed so an automatic shutter can
                // refuse to resume an exposure that has already reached its target sample count.
                ItemStack? resumeCameraStack = CameraItemHelper.GetActiveCameraStack(clientApi);
                IExposureStopCondition? resumeCondition = resumeCameraStack != null
                    ? ShutterSeam.PolicyProvider?.Resolve(resumeCameraStack, existingAcc.TargetFrames)
                    : null;

                if (!(resumeCondition?.CanResume(existingAcc.FramesAccumulated, existingAcc.TargetFrames) ?? true))
                {
                    if (!silentIfBusy)
                        ShowShutterGateMessageThrottled(Lang.Get("photocore:msg-exposure-target-reached"));
                    return false;
                }

                if (existingAcc.FramesAccumulated >= _maxFrames)
                {
                    if (!silentIfBusy)
                        ShowShutterGateMessageThrottled(Lang.Get("photocore:msg-plate-max-frames", _maxFrames));
                    return false;
                }

                if (existingAcc is ViewportExposureAccumulator viewportExisting)
                {
                    viewportExisting.StopCondition = resumeCondition;
                    viewportExisting.OnAutoPause = () => OnAccumulatorAutoPause(viewportExisting, exposureId);
                    viewportExisting.LiveEffectsSource = _owner.Capture._virtualExposureRenderer;
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

            // Resolve the emulsion physics from the loaded plate's chemistry (baseline plates are always
            // iodide; other heads' chemistries resolve here). Falls back to Iodide for absent/unknown.
            EmulsionProfile profile = EmulsionProfile.Resolve(PlateAttributes.GetChemistry(loadedPlateStack));

            // When Prime() was called, the PBO ring is already warm so the first sample tick maps
            // a real frame immediately — no sync GL.ReadPixels stall, no 2-kick priming gap.
            ViewportExposureAccumulator newAcc = _owner.Capture._primedViewportAccumulator ?? new ViewportExposureAccumulator(clientApi);
            _owner.Capture._primedViewportAccumulator = null;
            newAcc.ExposurePreviewSink = _owner.Capture._virtualCameraPreviewRenderer;
            newAcc.LiveEffectsSource = _owner.Capture._virtualExposureRenderer;
            newAcc.OnAutoHalt = () => OnAccumulatorAutoHalt(byEntity, newAcc, exposureId);
            newAcc.OnAutoPause = () => OnAccumulatorAutoPause(newAcc, exposureId);

            // Install the camera's shutter stop policy (timed / automatic). Null on baseline ⇒ manual shutter.
            ItemStack? cameraStack = CameraItemHelper.GetActiveCameraStack(clientApi);
            newAcc.StopCondition = cameraStack != null
                ? ShutterSeam.PolicyProvider?.Resolve(cameraStack, newAcc.TargetFrames)
                : null;

            newAcc.Start(profile);
            Diag($"handheld start: chan={(_owner.ClientChannel == null ? "null" : _owner.ClientChannel.Connected ? "connected" : "NOT-connected")} " +
                 $"profile={profile.Name} target={newAcc.TargetFrames}");

            if (crossCameraBlob != null)
                newAcc.PrimeFromPartial(crossCameraBlob);

            ViewfinderExposureRegistry.Register(exposureId, newAcc);
            _owner.Capture.ActiveAccumulator = newAcc;
            _owner.Capture.ActiveExposureId = exposureId;

            _owner.Capture.MaybeShowF4GuiLessTip();
            SendExposureStatePacket(isExposing: true, newAcc.FramesAccumulated, exposureId, newAcc.TargetFrames);

            return true;
        }

        // Called by the accumulator's auto-halt callback -- the hard accumulation cap was reached.
        // This is the one condition, across every camera type, that seals the plate as terminally
        // Exposed; only developing it in a tray afterward creates a photo.
        private void OnAccumulatorAutoHalt(EntityAgent byEntity, ViewportExposureAccumulator acc, string exposureId)
        {
            _suppressViewfinderUntilRmbReleased = true;
            HandleExposurePaused(acc, exposureId, reachedCap: true);
            if (_owner.Capture.IsViewfinderActive) _owner.Capture.EndViewfinderMode();
        }

        // Called by the accumulator's auto-pause callback (e.g. a timed-shutter burst elapsed). The
        // accumulator has already transitioned to Paused; this persists the partial and notifies the
        // server, mirroring the tail of a manual pause. The exposure stays resumable -- an automatic
        // shutter's own timed close is not the hard cap and must not seal the plate.
        private void OnAccumulatorAutoPause(ViewportExposureAccumulator acc, string exposureId)
        {
            HandleExposurePaused(acc, exposureId, reachedCap: false);
            if (!GetRightMouseDown() && _owner.Capture.IsViewfinderActive)
                _owner.Capture.EndViewfinderMode();
        }

        // Persists a paused accumulator's partial blob and notifies the server it is no longer exposing.
        // Shared by the manual LMB-pause path and the auto-pause (timed shutter) callback.
        private void HandleExposurePaused(IGameplayExposureAccumulator acc, string exposureId, bool reachedCap)
        {
            if (acc is ViewportExposureAccumulator viewportAcc
                && acc.FramesAccumulated > 0
                && !string.IsNullOrEmpty(exposureId))
            {
                byte[]? blob = viewportAcc.ExportPartial();
                if (blob != null) SavePartialExposureAsync(exposureId, blob);
            }

            SendExposureStatePacket(isExposing: false, acc.FramesAccumulated, exposureId, acc.TargetFrames, reachedCap);
        }

        // The compress-and-write has no GL dependency once ExportPartial()'s readback has already
        // happened, so it doesn't need to hold up the frame that just paused an exposure. Chained onto
        // this exposureId's previous save (see _pendingExposureSaves) so writes land in call order even
        // if the thread pool would otherwise run them out of order. Chat is not safe to touch off the
        // main thread, so a failure is bounced back via EnqueueMainThreadTask.
        private void SavePartialExposureAsync(string exposureId, byte[] blob)
        {
            ICoreClientAPI? clientApi = _owner.ClientApi;

            lock (_pendingExposureSavesLock)
            {
                Task previous = _pendingExposureSaves.TryGetValue(exposureId, out Task? p) ? p : Task.CompletedTask;
                Task next = previous.ContinueWith(_ =>
                {
                    if (ExposureAccumulationStore.Save(exposureId, blob)) return;
                    clientApi?.Event.EnqueueMainThreadTask(
                        () => clientApi.ShowChatMessage(Lang.Get("photocore:msg-exposure-save-failed")),
                        "photocore-exposure-save-failed");
                }, TaskScheduler.Default);

                _pendingExposureSaves[exposureId] = next;
            }
        }

        private void SendExposureStatePacket(bool isExposing, int exposedFrames, string exposureId, int targetFrames, bool reachedCap = false)
        {
            _owner.ClientChannel?.SendPacket(new ExposureStatePacket
            {
                IsExposing = isExposing,
                ExposureId = exposureId,
                ExposedFrames = exposedFrames,
                TargetFrames = targetFrames,
                ReachedCap = reachedCap
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
            Diag($"mounted recv: rendererNull={renderer == null} packetNull={packet == null} " +
                 $"isExposing={packet?.IsExposing} hasCamState={packet?.HasCameraState} prepIdle={packet?.PrepareIdlePreview} " +
                 $"pendingState={(_pendingMountedCameraState == null ? "null" : "set")} " +
                 $"chan={(_owner.ClientChannel == null ? "null" : _owner.ClientChannel.Connected ? "connected" : "NOT-connected")}");
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
                    // Resolve the mounted plate's chemistry (threaded from the server) so the idle/live
                    // preview emulsion matches the active plate; empty (no plate) falls back to iodide.
                    EmulsionProfile previewProcess = EmulsionProfile.Resolve(packet.Chemistry);

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
                    ShowShutterGateMessageThrottled(Lang.Get("photocore:msg-exposure-already-active"));
                    return;
                }

                // A Paused renderer can only be sitting here because of the resumable-in-place pause
                // below (Discard() always forces State back to Idle first), so its buffer and camera
                // are still valid for this exposure -- resume directly instead of tearing down and
                // reloading the partial from disk.
                if (renderer.State == ExposureState.Paused)
                {
                    renderer.Resume();
                    SendExposureStatePacket(true, renderer.FramesAccumulated, _mountedExposureId, renderer.CapFrameCount);
                    return;
                }

                if (_pendingMountedCameraState is VirtualCameraState cameraState)
                {
                    var clientApi = _owner.ClientApi;
                    if (clientApi == null) return;

                    // Resolve the mounted plate's chemistry (threaded from the server) so the exposure uses
                    // its per-process sample count and duration; empty (no plate) falls back to iodide.
                    EmulsionProfile profile = EmulsionProfile.Resolve(packet.Chemistry);

                    renderer.ApplyFinishing = false;
                    renderer.ExposurePreviewSink = _owner.Capture._virtualCameraPreviewRenderer;
                    renderer.Start(cameraState, profile);
                    Diag($"mounted start: renderer.Start called, state={renderer.State} profile={profile.Name}");
                    _maxFrames = PhotocoreConfigAccess.ResolveClientConfig(clientApi)?.Viewfinder?.MaxAccumulatedFrames
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
                            ShowShutterGateMessageThrottled(Lang.Get("photocore:msg-plate-max-frames", _maxFrames));
                            renderer.Discard();
                            return;
                        }
                    }

                    SendExposureStatePacket(true, renderer.FramesAccumulated, _mountedExposureId, renderer.CapFrameCount);
                }
                else
                {
                    Diag("mounted start ABORTED: IsExposing but _pendingMountedCameraState is null (no camera state seeded)");
                }
                return;
            }

            // Nothing to stop unless an exposure is live or held paused. A paused renderer can reach
            // here because a plain pause now keeps its buffer resident (see below), so a later
            // unload/pickup/break stop must still run to release that buffer.
            if (renderer.State != ExposureState.Capturing && renderer.State != ExposureState.Paused) return;

            bool wasCapturing = renderer.State == ExposureState.Capturing;
            int framesAtPause = renderer.FramesAccumulated;

            if (wasCapturing)
            {
                renderer.Pause();

                if (framesAtPause > 0 && !string.IsNullOrEmpty(_mountedExposureId))
                {
                    byte[]? blob = renderer.ExportPartial();
                    if (blob != null) SavePartialExposureAsync(_mountedExposureId, blob);
                }
            }

            // Keep the GPU buffer and camera resident only for a plain pause of the same exposure that
            // is still mounted, so a resume skips tearing down and reloading the just-saved partial from
            // disk. Every other stop (plate unload/swap, camera pickup, block broken) must discard, or a
            // later exposure could resume onto this one's accumulated frames.
            bool sameExposureStillMounted = packet.HasMountBlock
                && !string.IsNullOrEmpty(packet.ExposureId)
                && string.Equals(packet.ExposureId, _mountedExposureId, StringComparison.Ordinal);

            if (!sameExposureStillMounted)
                renderer.Discard();

            // Only the live capture just paused needs to report its frame count to the server; an
            // already-paused renderer being torn down has nothing new to persist (matching the prior
            // early-return that sent no packet in that case).
            if (wasCapturing)
                SendExposureStatePacket(false, framesAtPause, _mountedExposureId, renderer.CapFrameCount);
        }

        // Only reached via OnClientViewfinderTick's Done-state check, which for the mounted renderer
        // is only ever set by its own cap-halt (VirtualExposureRenderer.CompleteAutoStop) -- so this
        // is always the hard-cap path, not a resumable pause.
        private void PersistPartialMountedExposure()
        {
            var renderer = _owner.Capture._virtualExposureRenderer;
            if (renderer == null) return;

            if (renderer.FramesAccumulated > 0 && !string.IsNullOrEmpty(_mountedExposureId))
            {
                byte[]? blob = renderer.ExportPartial();
                if (blob != null) SavePartialExposureAsync(_mountedExposureId, blob);
            }

            SendExposureStatePacket(false, renderer.FramesAccumulated, _mountedExposureId, renderer.CapFrameCount, reachedCap: true);

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
