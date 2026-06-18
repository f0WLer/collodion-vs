using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Collodion.AdminTooling;
using Collodion.ImageEffects;
using Collodion.Exposure;

namespace Collodion.CameraCapture
{
    /// <summary>
    /// Persistent renderer that drives a <see cref="VirtualCamera"/> across consecutive game frames,
    /// accumulating pixel data via a <see cref="GpuExposureAccumulator"/>.
    /// Registered at <c>EnumRenderStage.Before</c> while a session is active.
    /// <para>Lifecycle: <see cref="Start"/> → Capturing → optional <see cref="Pause"/>/<see cref="Resume"/> →
    /// <see cref="Stop"/> (preserves buffer) or <see cref="Discard"/> (clears buffer).  <see cref="Export"/>
    /// can be called any time frames exist; <see cref="ExportPartial"/> serializes the raw buffer for
    /// cross-session persistence.</para>
    /// <para><see cref="ApplyFinishing"/> defaults to <see langword="false"/>; finishing is applied
    /// by <see cref="PartialExposureSealer"/> at development time, not here.</para>
    /// </summary>
    internal sealed class VirtualExposureRenderer : IRenderer, IDisposable
    {
        private readonly ICoreClientAPI _clientApi;
        private readonly ClientPlatformWindows _platform;
        private readonly ClientMain _main;
        private readonly ImageEffectsConfig _baselineEffects;

        private VirtualCamera? _camera;
        private GpuExposureAccumulator? _buffer;
        private PlateProcessProfile _process = PlateProcessProfile.Iodide;
        private float _elapsedSinceLastSample;
        private float _elapsedSinceLastPreview;

        // Wall-clock shutter timing (milliseconds from _capi.ElapsedMilliseconds).
        private long _shutterStartMs;
        private long _shutterEndMs;
        private long _pauseStartedMs;
        private long _shutterFrozenMs;

        private bool _disposed;

        // When set, the exposure renderer pushes developed preview frames here while capturing,
        // keeping the debug preview window live during long exposures.
        internal IExposurePreviewSink? ExposurePreviewSink { get; set; }

        // Process profile applied to the current or next exposure session.
        // Controls timing (duration, sample count) and emulsion response (spectral weights, H&D curve).
        internal PlateProcessProfile ActiveProcess => _process;

        internal ExposureState State { get; private set; } = ExposureState.Idle;
        // Set when a render exception transitions the session to Faulted; cleared on Start.
        internal string? LastFaultMessage { get; private set; }
        internal int FramesAccumulated => _buffer?.FramesAccumulated ?? 0;
        internal int CapFrameCount => _process.SampleCount;

        private long GetEffectiveShutterNowMs()
            => _shutterFrozenMs != 0 ? _shutterFrozenMs : _clientApi.ElapsedMilliseconds;

        // Wall-clock time elapsed since shutter open. Returns 0 when no exposure is active.
        internal float ElapsedSeconds
            => _shutterStartMs == 0 ? 0f : Math.Max(0f, (GetEffectiveShutterNowMs() - _shutterStartMs) / 1000f);

        // Post-development finishing toggle. When off, exposure preview/export stop after emulsion develop.
        internal bool ApplyFinishing = false;

        // Tunable physics + chemistry overrides for this session, applied to each new buffer.
        // The admin physics dialog and the live preview read/tune this directly.
        internal ExposurePhysicsConfig Physics { get; } = new();

        // Live, session-only effects (seeded from wetplate.json at construction). The exposure-physics
        // dialog mutates this directly so the preview reflects edits immediately; it is never persisted.
        internal ImageEffectsConfig Effects => _baselineEffects;

        // Sets a named physics flag and reapplies it to the live buffer (if any).
        internal bool SetPhysics(string flag, bool value)
        {
            bool ok = Physics.SetPhysics(flag, value);
            if (ok && _buffer != null) Physics.ApplyPhysics(_buffer);
            return ok;
        }

        // Sets a named chemistry override and reapplies it to the live buffer (if any).
        internal bool SetChemistry(string param, float value)
        {
            bool ok = Physics.SetChemistry(param, value);
            if (ok && _buffer != null) Physics.Apply(_buffer, _process);
            return ok;
        }

        // Clears all chemistry overrides and restores process defaults on the live buffer.
        internal void ResetChemistryOverrides()
        {
            Physics.ResetChemistryOverrides();
            if (_buffer != null) Physics.Apply(_buffer, _process);
        }

        // Re-resolves the buffer immediately if capturing, otherwise resets the idle-preview timer.
        internal void RequestPreviewRefresh()
        {
            if (State == ExposureState.Capturing && _buffer?.FramesAccumulated > 0)
                PushPreviewFrame();
            ExposurePreviewSink?.ForceRefreshNextFrame();
        }

        public double RenderOrder => 0.4;
        public int RenderRange => 0;

        internal VirtualExposureRenderer(ICoreClientAPI capi)
        {
            _clientApi = capi;
            _main = (ClientMain)capi.World;
            _platform = (ClientPlatformWindows)_main.Platform;
            _baselineEffects = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
        }

        private void CompleteAutoStop(long nowMs)
        {
            _shutterFrozenMs = nowMs;
            State = ExposureState.Done;
            PushPreviewFrame();
            _clientApi.Logger.Notification(
                $"photochemistry: {_process.Name} exposure complete — " +
                $"{_buffer?.FramesAccumulated ?? 0}/{_process.SampleCount} samples over {(nowMs - _shutterStartMs) / 1000f:F2}s. " +
                $"Use '.collodion exposure export' to save.");
        }

        /// <summary>
        /// Prepares the virtual camera for preview before an exposure begins.
        /// Only valid when <see cref="State"/> is <see cref="ExposureState.Idle"/>; no-op otherwise.
        /// Replaces any previously prepared camera.
        /// </summary>
        internal void PrepareCamera(VirtualCameraState cameraState)
        {
            if (State != ExposureState.Idle && State != ExposureState.Paused) return;
            DestroyCamera();
            VirtualCamera cam = new VirtualCamera(_clientApi, _platform, _main);
            cam.ApplyState(cameraState);
            cam.InitBuffer();
            _camera = cam;
        }

        /// <summary>
        /// Returns the prepared virtual camera when idle (no active or completed exposure),
        /// allowing the preview renderer to render idle viewfinder frames.
        /// Returns <see langword="false"/> during any active, paused, done, or faulted session.
        /// </summary>
        internal bool TryGetIdleCameraForPreview(out VirtualCamera camera)
        {
            if (State == ExposureState.Idle && _camera != null)
            {
                camera = _camera;
                return true;
            }
            camera = null!;
            return false;
        }

        /// <summary>
        /// Returns <see langword="true"/> when a virtual camera is prepared and no exposure session is active.
        /// Use this for boolean checks; use <see cref="TryGetIdleCameraForPreview"/> when you also need the camera instance.
        /// </summary>
        internal bool HasIdleCameraForPreview => State == ExposureState.Idle && _camera != null;

        /// <summary>
        /// Destroys the prepared virtual camera and returns it to an uninitialized state.
        /// Call when the owning mounted-camera block is removed.
        /// No-op when no camera is prepared.
        /// </summary>
        internal void ClearCamera() => DestroyCamera();

        /// <summary>Starts a new exposure session with the given camera state, chemistry, and stop policy. Any previous session is discarded.</summary>
        internal void Start(VirtualCameraState cameraState, PlateProcessProfile process)
        {
            Discard(); // Clears buffer/state but preserves any prepared _camera.
            _process = process;
            LastFaultMessage = null;
            _elapsedSinceLastSample  = 0f;
            _elapsedSinceLastPreview = 0f;

            long now = _clientApi.ElapsedMilliseconds;
            _shutterStartMs = now;
            _pauseStartedMs = 0;
            _shutterFrozenMs = 0;

            if (_camera == null)
            {
                // No camera prepared by PrepareCamera() — create one from the supplied state (legacy/admin path).
                VirtualCamera cam = new VirtualCamera(_clientApi, _platform, _main);
                cam.ApplyState(cameraState);
                cam.InitBuffer();
                _camera = cam;
            }

            AllocateBuffer();
            ExposurePreviewSink?.BeginExposurePassthrough();
            State = ExposureState.Capturing;
        }

        /// <summary>Pauses frame accumulation and freezes the shutter timer. Only valid in <see cref="ExposureState.Capturing"/> state.</summary>
        internal void Pause()
        {
            if (State == ExposureState.Capturing)
            {
                _pauseStartedMs = _clientApi.ElapsedMilliseconds;
                _shutterFrozenMs = _pauseStartedMs;
                State = ExposureState.Paused;
            }
        }

        /// <summary>Resumes frame accumulation and extends the shutter window by the pause duration. Only valid in <see cref="ExposureState.Paused"/> state.</summary>
        internal void Resume()
        {
            if (State == ExposureState.Paused)
            {
                // Extend the shutter window by however long we were paused.
                long pausedFor = _clientApi.ElapsedMilliseconds - _pauseStartedMs;
                _shutterStartMs += pausedFor;
                _shutterEndMs   += pausedFor;
                _pauseStartedMs = 0;
                _shutterFrozenMs = 0;
                State = ExposureState.Capturing;
            }
        }

        /// <summary>
        /// Closes the shutter and transitions to <see cref="ExposureState.Done"/>.
        /// Drains any in-flight readback PBOs first so no samples are lost.
        /// The accumulated buffer and the prepared virtual camera are both preserved;
        /// call <see cref="Discard"/> to clear the buffer and return to <see cref="ExposureState.Idle"/>.
        /// </summary>
        internal void Stop()
        {
            _shutterFrozenMs = _clientApi.ElapsedMilliseconds;
            // The virtual camera is kept alive so the preview can continue after shutter close.
            State = ExposureState.Done;

            int frames = _buffer?.FramesAccumulated ?? 0;
            long nowMs = _clientApi.ElapsedMilliseconds;
            float elapsed = _shutterStartMs == 0 ? 0f : (nowMs - _shutterStartMs) / 1000f;
            _clientApi.Logger.Notification(
                $"photochemistry: {_process.Name} exposure stopped — " +
                $"{frames}/{_process.SampleCount} samples over {elapsed:F2}s. " +
                $"Use '.collodion exposure export' to save.");
        }

        /// <summary>
        /// Clears the accumulated buffer and returns to <see cref="ExposureState.Idle"/>.
        /// The prepared virtual camera is preserved so idle preview rendering continues uninterrupted.
        /// Call <see cref="ClearCamera"/> separately when the mounted-camera block is removed.
        /// </summary>
        internal void Discard()
        {
            _buffer?.Dispose();
            _buffer = null;
            _shutterStartMs = 0;
            _shutterEndMs   = 0;
            _pauseStartedMs = 0;
            _shutterFrozenMs = 0;
            ExposurePreviewSink?.EndExposurePassthrough();
            State = ExposureState.Idle;
        }

        /// <summary>Clears accumulated frames and restarts capture from the same camera position. No-op when no camera is alive.</summary>
        internal void Reset()
        {
            if (_buffer == null || _camera == null) return;
            _buffer.Reset();
            _elapsedSinceLastSample  = 0f;
            _elapsedSinceLastPreview = 0f;
            long now = _clientApi.ElapsedMilliseconds;
            _shutterStartMs = now;
            _pauseStartedMs = 0;
            _shutterFrozenMs = 0;
            State = ExposureState.Capturing;
        }

        internal string Export(ImageEffectsConfig? effectsOverride = null)
        {
            if (_buffer == null || _buffer.FramesAccumulated == 0)
                throw new InvalidOperationException("No frames accumulated.");

            int maxDimension = CollodionConfigAccess.ResolveClientConfig(_clientApi)?.Viewfinder?.PhotoCaptureMaxDimension
                ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;

            return ExposureSeal.ToPhoto(_buffer, maxDimension, "exposure-export", _baselineEffects, effectsOverride, ApplyFinishing);
        }

        /// <summary>
        /// Serializes the raw float accumulation sums to a binary blob for cross-session persistence.
        /// Returns <see langword="null"/> when no frames have been accumulated or the buffer is not allocated.
        /// The blob is self-describing and can be passed to <see cref="PrimeFromPartial"/> after a future <see cref="Start"/>.
        /// </summary>
        internal byte[]? ExportPartial()
        {
            return _buffer?.SerializeAccumulation();
        }

        /// <summary>
        /// Restores a previously serialized accumulation blob into the live buffer after <see cref="Start"/> is called.
        /// When the blob's dimensions do not match the current buffer (e.g. the screen was resized since the session was paused),
        /// the call is a no-op and the exposure continues from zero frames.
        /// </summary>
        internal void PrimeFromPartial(byte[] data)
        {
            if (_buffer == null) return;

            if (!_buffer.DeserializeAccumulation(data, out int restoredFrames))
            {
                _clientApi.Logger.Warning("photochemistry: partial exposure blob is incompatible with the current buffer dimensions — starting fresh.");
                return;
            }

            _clientApi.Logger.Notification($"photochemistry: restored {restoredFrames} accumulated frames from saved partial exposure.");
        }

        // Resolves and shapes one debug-preview frame using the same crop/scale/finishing policy
        // as the normal virtual camera preview path.
        // Normalises by actual frame count so the preview shows a "final-exposure" prediction
        // rather than the current underexposed state (which would be proportionally darker for
        // every frame before the target count is reached).
        private void PushPreviewFrame()
        {
            if (_buffer == null || ExposurePreviewSink == null || _buffer.FramesAccumulated == 0) return;

            ViewfinderConfig? cfg = CollodionConfigAccess.ResolveClientConfig(_clientApi)?.Viewfinder;
            // Skip the GPU→CPU resolve when no one is displaying the preview (DebugPreviewPeak off).
            if (!(cfg?.DebugPreviewPeak ?? false)) return;
            int maxDimension = cfg.DebugPreviewMaxDimension;

            SKBitmap developed = _buffer.Resolve();

            using (developed)
            {
                SKBitmap cropped = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(developed, maxDimension);
                try
                {
                    if (ApplyFinishing)
                    {
                        // Apply the live session effects (tuned by the exposure-physics dialog) directly,
                        // rather than re-reading wetplate.json every preview frame.
                        ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, "exposure-preview", _baselineEffects);
                    }
                    ExposurePreviewSink.StoreExposureFrame(cropped);
                }
                finally
                {
                    cropped.Dispose();
                }
            }
        }

        // Minimum wall-clock seconds between consecutive preview pushes.
        private const float PreviewCadenceSeconds = 0.25f;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (State != ExposureState.Capturing) return;
            if (_camera == null || _buffer == null) return;

            // Always advance both timers so preview cadence and sample interval track real time
            // independently of each other and of the game's frame rate.
            _elapsedSinceLastSample  += deltaTime;
            _elapsedSinceLastPreview += deltaTime;

            // Reinitialize FBO and buffer if the window was resized.
            // Mixed-dimension frames cannot be averaged so accumulated data must be discarded.
            if (_clientApi.Render.FrameWidth != _camera.fbo.Width || _clientApi.Render.FrameHeight != _camera.fbo.Height)
            {
                ReinitializeCameraAndBufferForResize();
                _clientApi.Logger.Warning("photochemistry: window resized during exposure — accumulated frames discarded.");
            }

            // Rate limiter: never sample faster than the process cadence.
            if (_elapsedSinceLastSample < _process.SampleInterval) return;
            _elapsedSinceLastSample -= _process.SampleInterval;

            try
            {
                _camera.RenderCameraInStoredDimension(deltaTime);

                // Clear the primary framebuffer that RenderCamera may have left in an intermediate state.
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                _buffer.Accumulate(_camera.fbo);
            }
            catch (Exception ex)
            {
                _clientApi.Logger.Error($"photochemistry: exposure frame {_buffer.FramesAccumulated} render failed: {ex}");
                LastFaultMessage = ex.Message;
                _shutterFrozenMs = _clientApi.ElapsedMilliseconds;
                State = ExposureState.Faulted;
                return;
            }

            // Hard cap: stop regardless of stop mode once MaxAccumulatedFrames is reached.
            int maxFrames = CollodionConfigAccess.ResolveClientConfig(_clientApi)?.Viewfinder?.MaxAccumulatedFrames
                ?? ViewfinderConfig.DefaultMaxAccumulatedFrames;
            if (_buffer.FramesAccumulated >= maxFrames)
            {
                CompleteAutoStop(_clientApi.ElapsedMilliseconds);
                return;
            }

            // Push preview on wall-clock cadence; only after new data has been accumulated
            // so the preview reflects the latest exposure state without redundant develop calls.
            if (ExposurePreviewSink != null && _buffer.FramesAccumulated > 0 &&
                (_buffer.FramesAccumulated == 1 || _elapsedSinceLastPreview >= PreviewCadenceSeconds))
            {
                _elapsedSinceLastPreview = 0f;
                PushPreviewFrame();
            }
        }

        private void DestroyCamera()
        {
            if (_camera == null) return;
            BestEffort.Try(null, "destroy virtual exposure camera", () => _camera.Destroy());
            _camera = null;
        }

        // Allocates a GpuExposureAccumulator sized to the current frame (possibly downsampled).
        private void AllocateBuffer()
        {
            int sourceW = _clientApi.Render.FrameWidth;
            int sourceH = _clientApi.Render.FrameHeight;
            int maxDim  = CollodionConfigAccess.ResolveClientConfig(_clientApi)?.Viewfinder?.ExposureReadbackMaxDimension
                          ?? ViewfinderConfig.DefaultExposureReadbackMaxDimension;

            GpuExposureAccumulator.ComputeTargetDimensions(sourceW, sourceH, maxDim, out int w, out int h);
            _buffer?.Dispose();
            var gpu = new GpuExposureAccumulator(_clientApi, w, h, _process.SampleCount);
            Physics.Apply(gpu, _process);
            _buffer = gpu;
        }

        private void ReinitializeCameraAndBufferForResize()
        {
            if (_camera == null) return;
            _camera.Destroy(); // Destroys the FBO; _camera object is reused (not nulled).
            _camera.InitBuffer();
            AllocateBuffer();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Discard();
            DestroyCamera();
        }
    }
}
