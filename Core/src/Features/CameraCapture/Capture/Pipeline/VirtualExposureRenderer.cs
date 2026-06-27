using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

using Photocore.ImageEffects;
using Photocore.Exposure;
using Photocore.Configuration;

namespace Photocore.CameraCapture
{
    internal sealed class VirtualExposureRenderer : IRenderer, IDisposable
    {
        private readonly ICoreClientAPI _clientApi;
        private readonly ClientPlatformWindows _platform;
        private readonly ClientMain _main;

        private VirtualCamera? _camera;
        private GpuExposureAccumulator? _buffer;
        private EmulsionProfile _process = EmulsionProfile.Iodide;
        private float _elapsedSinceLastSample;
        private float _elapsedSinceLastPreview;

        private long _shutterStartMs;
        private long _pauseStartedMs;
        private long _shutterFrozenMs;

        private bool _disposed;

        // Temporary startup-race diagnostics (gated on ShowDebugLogs). See the exposure-flakiness plan.
        private bool _diagFirstTick;
        private bool _diagFirstAccum;
        private static void Diag(string msg) =>
            PhotocoreModSystem.ClientInstance?.BestEffortLogger?.Notification("photocore[diag]: " + msg);

        internal IExposurePreviewSink? ExposurePreviewSink { get; set; }
        internal EmulsionProfile ActiveProcess => _process;

        internal ExposureState State { get; private set; } = ExposureState.Idle;
        internal string? LastFaultMessage { get; private set; }
        internal int FramesAccumulated => _buffer?.FramesAccumulated ?? 0;
        internal int CapFrameCount => _process.SampleCount;

        private long GetEffectiveShutterNowMs()
            => _shutterFrozenMs != 0 ? _shutterFrozenMs : _clientApi.ElapsedMilliseconds;

        internal float ElapsedSeconds
            => _shutterStartMs == 0 ? 0f : Math.Max(0f, (GetEffectiveShutterNowMs() - _shutterStartMs) / 1000f);

        internal bool ApplyFinishing = false;
        internal ExposurePhysicsConfig Physics { get; } = new();

        // The dialog edits these; SaveChemistryTuning commits them to the saved profile.
        private ImageEffectsConfig _previewEffects = new();
        private string _previewChemistryName = string.Empty;

        private void SeedPreviewFrom(string chemistry)
        {
            ChemistryProfile prof = ChemistryProfileRegistry.Instance.Get(chemistry);
            Physics.Chem = prof.ExposurePhysics.Clone();
            _previewEffects = prof.PostEffects.Clone();
            _previewChemistryName = chemistry;
        }

        // Only reseed when the chemistry changes — preserves in-progress dialog edits.
        private void EnsurePreviewFor(string chemistry)
        {
            if (!string.Equals(_previewChemistryName, chemistry, StringComparison.OrdinalIgnoreCase)) SeedPreviewFrom(chemistry);
        }

        internal ImageEffectsConfig PreviewEffects => _previewEffects;


        internal bool SetPhysics(string flag, bool value)
        {
            bool ok = Physics.SetPhysics(flag, value);
            if (ok && _buffer != null) Physics.ApplyPhysics(_buffer);
            return ok;
        }

        internal bool SetChemistry(string param, float value)
        {
            bool ok = Physics.SetChemistry(param, value);
            if (ok && _buffer != null) Physics.Apply(_buffer, _process);
            return ok;
        }

        internal void ResetChemistryOverrides()
        {
            Physics.ResetChemistryOverrides();
            if (_buffer != null) Physics.Apply(_buffer, _process);
        }

        // Ignored while an exposure is in flight so a live capture's profile/timing is never disturbed.
        internal void SetTuningChemistry(EmulsionProfile process)
        {
            if (State == ExposureState.Capturing || State == ExposureState.Paused) return;
            EnsurePreviewFor(process.Name);
            _process = ApplyTimingOverrides(process, Physics.Chem);
            if (_buffer != null) Physics.Apply(_buffer, _process);
            RequestPreviewRefresh();
        }

        // Ignored mid-exposure so a live capture's frame target isn't moved.
        internal void SetTuningTiming(int sampleCount, float durationSeconds)
        {
            if (State == ExposureState.Capturing || State == ExposureState.Paused) return;
            Physics.Chem.SampleCount     = Math.Max(1, sampleCount);
            Physics.Chem.DurationSeconds = Math.Max(0.05f, durationSeconds);
            _process = ApplyTimingOverrides(_process, Physics.Chem);
            RequestPreviewRefresh();
        }

        internal void SaveChemistryTuning()
        {
            ChemistryProfile prof = ChemistryProfileRegistry.Instance.Get(_process.Name);
            prof.ExposurePhysics.CopyFrom(Physics.Chem);
            prof.PostEffects = _previewEffects.Clone();
            ChemistryProfileRegistry.Instance.Save(_clientApi.Logger);
        }

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
            SeedPreviewFrom(_process.Name);
            _process = ApplyTimingOverrides(_process, Physics.Chem);
        }

        private static EmulsionProfile ApplyTimingOverrides(EmulsionProfile p, ChemistryOverrides ov) => ov.ApplyTimingTo(p);

        private void CompleteAutoStop(long nowMs)
        {
            _shutterFrozenMs = nowMs;
            State = ExposureState.Done;
            PushPreviewFrame();
            _clientApi.Logger.Notification(
                $"photocore: {_process.Name} exposure complete — " +
                $"{_buffer?.FramesAccumulated ?? 0}/{_process.SampleCount} samples over {(nowMs - _shutterStartMs) / 1000f:F2}s. " +
                $"Use '.collodion exposure export' to save.");
        }

        internal void PrepareCamera(VirtualCameraState cameraState)
        {
            if (State != ExposureState.Idle && State != ExposureState.Paused) return;
            DestroyCamera();
            VirtualCamera cam = new VirtualCamera(_clientApi, _platform, _main);
            cam.ApplyState(cameraState);
            cam.InitBuffer();
            _camera = cam;
        }

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

        internal bool HasIdleCameraForPreview => State == ExposureState.Idle && _camera != null;

        internal void ClearCamera() => DestroyCamera();

        internal void Start(VirtualCameraState cameraState, EmulsionProfile process)
        {
            Discard();
            EnsurePreviewFor(process.Name);
            _process = ApplyTimingOverrides(process, Physics.Chem);
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
            _diagFirstTick = false;
            _diagFirstAccum = false;
        }

        internal void Pause()
        {
            if (State == ExposureState.Capturing)
            {
                _pauseStartedMs = _clientApi.ElapsedMilliseconds;
                _shutterFrozenMs = _pauseStartedMs;
                State = ExposureState.Paused;
            }
        }

        internal void Resume()
        {
            if (State == ExposureState.Paused)
            {
                long pausedFor = _clientApi.ElapsedMilliseconds - _pauseStartedMs;
                _shutterStartMs += pausedFor;
                _pauseStartedMs = 0;
                _shutterFrozenMs = 0;
                State = ExposureState.Capturing;
            }
        }

        internal void Stop()
        {
            _shutterFrozenMs = _clientApi.ElapsedMilliseconds;
            // The virtual camera is kept alive so the preview can continue after shutter close.
            State = ExposureState.Done;

            int frames = _buffer?.FramesAccumulated ?? 0;
            long nowMs = _clientApi.ElapsedMilliseconds;
            float elapsed = _shutterStartMs == 0 ? 0f : (nowMs - _shutterStartMs) / 1000f;
            _clientApi.Logger.Notification(
                $"photocore: {_process.Name} exposure stopped — " +
                $"{frames}/{_process.SampleCount} samples over {elapsed:F2}s. " +
                $"Use '.collodion exposure export' to save.");
        }

        internal void Discard()
        {
            _buffer?.Dispose();
            _buffer = null;
            _shutterStartMs = 0;
            _pauseStartedMs = 0;
            _shutterFrozenMs = 0;
            ExposurePreviewSink?.EndExposurePassthrough();
            // Camera preserved so idle preview continues.
            State = ExposureState.Idle;
        }

        internal string Export(ImageEffectsConfig? effectsOverride = null)
        {
            if (_buffer == null || _buffer.FramesAccumulated == 0)
                throw new InvalidOperationException("No frames accumulated.");

            int maxDimension = PhotocoreConfigAccess.ResolveClientConfig(_clientApi)?.Viewfinder?.PhotoCaptureMaxDimension
                ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;

            return ExposureSeal.ToPhoto(_buffer, maxDimension, "exposure-export", effectsOverride ?? PreviewEffects, ApplyFinishing);
        }

        internal byte[]? ExportPartial()
        {
            return _buffer?.SerializeAccumulation();
        }

        internal void PrimeFromPartial(byte[] data)
            => ExposureFrameOps.RestorePartial(_buffer, _clientApi.Logger, data);

        private void PushPreviewFrame()
            => ExposureFrameOps.PublishDevelopedPreview(_clientApi, _buffer, ExposurePreviewSink, ApplyFinishing, PreviewEffects);

        // Minimum wall-clock seconds between consecutive preview pushes.
        private const float PreviewCadenceSeconds = 0.25f;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (State != ExposureState.Capturing) return;
            if (_camera == null || _buffer == null) return;

            if (!_diagFirstTick) { _diagFirstTick = true; Diag("renderer tick: first Capturing OnRenderFrame reached"); }

            // Always advance both timers so preview cadence and sample interval track real time
            // independently of each other and of the game's frame rate.
            _elapsedSinceLastSample  += deltaTime;
            _elapsedSinceLastPreview += deltaTime;

            // Mixed-dimension frames cannot be averaged so accumulated data must be discarded.
            if (_clientApi.Render.FrameWidth != _camera.fbo.Width || _clientApi.Render.FrameHeight != _camera.fbo.Height)
            {
                ReinitializeCameraAndBufferForResize();
                _clientApi.Logger.Warning("photocore: window resized during exposure — accumulated frames discarded.");
            }

            if (_elapsedSinceLastSample < _process.SampleInterval) return;
            _elapsedSinceLastSample -= _process.SampleInterval;

            try
            {
                _camera.RenderCameraInStoredDimension(deltaTime);

                // Clear the primary framebuffer that RenderCamera may have left in an intermediate state.
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                _buffer.Accumulate(_camera.fbo);
                if (!_diagFirstAccum) { _diagFirstAccum = true; Diag($"renderer accumulate: first frame accumulated, frames={_buffer.FramesAccumulated}"); }
            }
            catch (Exception ex)
            {
                _clientApi.Logger.Error($"photocore: exposure frame {_buffer.FramesAccumulated} render failed: {ex}");
                LastFaultMessage = ex.Message;
                _shutterFrozenMs = _clientApi.ElapsedMilliseconds;
                State = ExposureState.Faulted;
                return;
            }

            int maxFrames = PhotocoreConfigAccess.ResolveClientConfig(_clientApi)?.Viewfinder?.MaxAccumulatedFrames
                ?? ViewfinderConfig.DefaultMaxAccumulatedFrames;
            if (_buffer.FramesAccumulated >= maxFrames)
            {
                CompleteAutoStop(_clientApi.ElapsedMilliseconds);
                return;
            }

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

        private void AllocateBuffer()
        {
            int sourceW = _clientApi.Render.FrameWidth;
            int sourceH = _clientApi.Render.FrameHeight;
            int maxDim  = PhotocoreConfigAccess.ResolveClientConfig(_clientApi)?.Viewfinder?.ExposureReadbackMaxDimension
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
