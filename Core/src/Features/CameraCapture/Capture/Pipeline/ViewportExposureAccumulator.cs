using Vintagestory.API.Client;
using Photochemistry.AdminTooling;
using Photochemistry.ImageEffects;
using Photochemistry.Exposure;

namespace Photochemistry.CameraCapture
{
    internal sealed class ViewportExposureAccumulator : IGameplayExposureAccumulator, IRenderer
    {
        private readonly ICoreClientAPI _capi;
        private GpuExposureAccumulator? _buffer;
        private EmulsionProfile _process;
        private float _elapsedSinceLastSample;
        private float _elapsedSinceLastPreview;
        private float _elapsedCaptureSeconds;
        private float _elapsedSinceResume;
        private bool _rendererRegistered;
        private bool _disposed;

        internal Action? OnAutoHalt { get; set; }
        internal Action? OnAutoPause { get; set; }

        // Null falls back to hard-cap auto-seal (manual shutter).
        internal IExposureStopCondition? StopCondition { get; set; }

        internal IExposurePreviewSink? ExposurePreviewSink { get; set; }

        public ExposureState State { get; private set; } = ExposureState.Idle;
        public bool IsCapturing => State == ExposureState.Capturing;
        public int FramesAccumulated => _buffer?.FramesAccumulated ?? 0;
        public int TargetFrames => _process.SampleCount;
        private int _maxFrames;

        public double RenderOrder => 0.3;
        public int RenderRange => 0;

        internal ViewportExposureAccumulator(ICoreClientAPI capi)
        {
            _capi = capi;
        }

        // When the exposure-physics dialog is open, use its live working copy so slider edits are
        // reflected immediately in the handheld preview without requiring a Save Profile round-trip.
        internal VirtualExposureRenderer? LiveEffectsSource { get; set; }

        private ImageEffectsConfig Effects
            => LiveEffectsSource?.PreviewEffects ?? ChemistryProfileRegistry.Instance.Get(_process.Name).PostEffects;

        // Pre-allocates GPU resources to avoid a first-frame stall when the shutter opens.
        internal void Prime()
        {
            if (_disposed) return;
            int w = Math.Max(1, _capi.Render.FrameWidth);
            int h = Math.Max(1, _capi.Render.FrameHeight);
            // Prime with the baseline sample count so the common iodide exposure reuses this buffer on Start
            // without reallocating. A different chemistry recreates it there (count mismatch) — still correct.
            EnsureGpuAccumulator(w, h, EmulsionProfile.Iodide.SampleCount);
            RegisterRenderer();
        }

        internal void Start(EmulsionProfile process)
        {
            if (_disposed) return;
            if (State == ExposureState.Paused) { Resume(); return; }
            if (State == ExposureState.Capturing) return;

            // Capture + resolve from the plate chemistry's SAVED profile (config): its physics flags,
            // per-parameter overrides, and shutter timing — identical to how the mounted/tray path develops.
            ChemistryOverrides cfg = ChemistryProfileRegistry.Instance.Get(process.Name).ExposurePhysics;
            _process = cfg.ApplyTimingTo(process);
            _elapsedSinceLastSample = 0f;
            _elapsedSinceLastPreview = 0f;
            _elapsedCaptureSeconds = 0f;
            _elapsedSinceResume = 0f;

            int w = Math.Max(1, _capi.Render.FrameWidth);
            int h = Math.Max(1, _capi.Render.FrameHeight);
            EnsureGpuAccumulator(w, h, _process.SampleCount);
            new ExposurePhysicsConfig { Chem = cfg }.Apply(_buffer!, _process);
            _buffer!.Reset(); // clear any frames accumulated during priming

            // Register renderer only if Prime() hasn’t already done so during viewfinder aiming.
            if (!_rendererRegistered) RegisterRenderer();
            State = ExposureState.Capturing;
            ViewportExposureSuppressContext.ExposureCapturing = true;
            ExposurePreviewSink?.BeginExposurePassthrough();

            _maxFrames = PhotochemistryConfigAccess.ResolveClientConfig(_capi)?.Viewfinder?.MaxAccumulatedFrames
                ?? ViewfinderConfig.DefaultMaxAccumulatedFrames;
        }

        public void Pause()
        {
            if (State == ExposureState.Capturing)
            {
                State = ExposureState.Paused;
                ViewportExposureSuppressContext.ExposureCapturing = false;
                ExposurePreviewSink?.EndExposurePassthrough();
            }
        }

        public void Resume()
        {
            if (State == ExposureState.Paused)
            {
                State = ExposureState.Capturing;
                _elapsedSinceResume = 0f;
                StopCondition?.OnResumed();
                ViewportExposureSuppressContext.ExposureCapturing = true;
                ExposurePreviewSink?.BeginExposurePassthrough();
            }
        }

        public void Stop()
        {
            // Done: renderer unregistration was already deferred via EnqueueMainThreadTask in the
            // auto-halt path; calling UnregisterRenderer() here from within the render loop would
            // crash the game. Idle: nothing to do.
            if (State == ExposureState.Idle || State == ExposureState.Done) return;
            ViewportExposureSuppressContext.ExposureCapturing = false;
            UnregisterRenderer();
            ExposurePreviewSink?.EndExposurePassthrough();
            State = ExposureState.Done;
        }

        public string Export(ImageEffectsConfig? effectsOverride = null)
        {
            if (_buffer == null || _buffer.FramesAccumulated == 0)
                throw new InvalidOperationException("ViewportExposureAccumulator: no frames accumulated.");

            int maxDim = PhotochemistryConfigAccess.ResolveClientConfig(_capi)?.Viewfinder?.PhotoCaptureMaxDimension
                ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;

            return ExposureSeal.ToPhoto(_buffer, maxDim, "viewport-exposure", effectsOverride ?? Effects);
        }

        internal byte[]? ExportPartial()
        {
            return _buffer?.SerializeAccumulation();
        }

        internal void PrimeFromPartial(byte[] data)
            => ExposureFrameOps.RestorePartial(_buffer, _capi.Logger, data);

        private const float PreviewCadenceSeconds = 0.25f;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (State != ExposureState.Capturing || _buffer == null) return;

            _elapsedCaptureSeconds += deltaTime;
            _elapsedSinceResume += deltaTime;
            _elapsedSinceLastSample += deltaTime;
            _elapsedSinceLastPreview += deltaTime;

            if (_elapsedSinceLastSample < _process.SampleInterval) return;
            _elapsedSinceLastSample -= _process.SampleInterval;

            int w = _capi.Render.FrameWidth;
            int h = _capi.Render.FrameHeight;

            // GPU blit scales into the fixed-size staging FBO, so viewport resize needs no special handling.
            _buffer.Accumulate(0, w, h);

            ExposureStopAction action = StopCondition?.Evaluate(_buffer.FramesAccumulated, _elapsedSinceResume, _maxFrames, TargetFrames)
                ?? (_buffer.FramesAccumulated >= _maxFrames ? ExposureStopAction.AutoSeal : ExposureStopAction.Continue);

            if (action == ExposureStopAction.AutoSeal)
            {
                CompleteAutoStop();
                return;
            }
            if (action == ExposureStopAction.AutoPause)
            {
                CompleteAutoPause();
                return;
            }

            if (_elapsedSinceLastPreview >= PreviewCadenceSeconds)
            {
                _elapsedSinceLastPreview = 0f;
                PushPreviewFrame();
            }
        }

        private void CompleteAutoStop()
        {
            ViewportExposureSuppressContext.ExposureCapturing = false;
            State = ExposureState.Done;
            ExposurePreviewSink?.EndExposurePassthrough();
            // Clear the flag NOW (before OnAutoHalt fires) so any Dispose() call triggered
            // by the auto-stop callback chain (e.g. Registry.Remove → Dispose) hits the
            // early-return guard in UnregisterRenderer() instead of crashing the iterator.
            // The deferred task calls the API directly because the flag is already false.
            _rendererRegistered = false;
            _capi.Event.EnqueueMainThreadTask(
                () => _capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit),
                "photochemistry-unregister-exposure");
            OnAutoHalt?.Invoke();
        }

        // Keep the buffer and renderer alive so the exposure can resume — mirrors Pause().
        private void CompleteAutoPause()
        {
            State = ExposureState.Paused;
            ViewportExposureSuppressContext.ExposureCapturing = false;
            ExposurePreviewSink?.EndExposurePassthrough();
            OnAutoPause?.Invoke();
        }

        // Mirrors the mounted-camera preview gate: respect ApplyFinishing when the dialog is wired in.
        private void PushPreviewFrame()
            => ExposureFrameOps.PublishDevelopedPreview(
                _capi, _buffer, ExposurePreviewSink,
                LiveEffectsSource == null || LiveEffectsSource.ApplyFinishing, Effects);

        private void EnsureGpuAccumulator(int sourceWidth, int sourceHeight, int sampleCount)
        {
            int maxDimension = PhotochemistryConfigAccess.ResolveClientConfig(_capi)?.Viewfinder?.ExposureReadbackMaxDimension
                ?? ViewfinderConfig.DefaultExposureReadbackMaxDimension;
            GpuExposureAccumulator.ComputeTargetDimensions(sourceWidth, sourceHeight, maxDimension, out int w, out int h);
            // Recreate not only on resize but also when the requested sample count differs from the existing
            // buffer's. _targetSampleCount is fixed at construction and drives preview/seal normalization
            // (1/_targetSampleCount); a buffer primed with a placeholder count would otherwise be reused with
            // the wrong reference, making the exposure start at baseline and overexpose without bound.
            if (_buffer == null || _buffer.Width != w || _buffer.Height != h || _buffer.TargetSampleCount != sampleCount)
            {
                _buffer?.Dispose();
                _buffer = new GpuExposureAccumulator(_capi, w, h, sampleCount);
            }
        }

        private void RegisterRenderer()
        {
            if (_rendererRegistered) return;
            _capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "photochemistry-viewport-exposure");
            _rendererRegistered = true;
        }

        private void UnregisterRenderer()
        {
            if (!_rendererRegistered) return;
            _capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
            _rendererRegistered = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ViewportExposureSuppressContext.ExposureCapturing = false;
            UnregisterRenderer();
            ExposurePreviewSink?.EndExposurePassthrough();
            _buffer?.Dispose();
            _buffer = null;
            State = ExposureState.Idle;
        }
    }
}
