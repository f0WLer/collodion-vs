using SkiaSharp;
using Vintagestory.API.Client;
using Collodion.AdminTooling;
using Collodion.ImageEffects;
using Collodion.Exposure;

namespace Collodion.CameraCapture
{
    /// <summary>
    /// Accumulates frames from the player's live viewport into a <see cref="GpuExposureAccumulator"/>.
    /// Registered as an <c>IRenderer</c> at <c>EnumRenderStage.AfterBlit</c> while actively capturing;
    /// blits the back buffer each sample interval directly into the GPU accumulator’s RGBA32F ping-pong FBOs
    /// via a GLSL accumulate shader. No CPU readback occurs until <see cref="Export"/> at shutter close.
    /// <para>Lifecycle: <see cref="Start"/> → <see cref="ExposureState.Capturing"/> → optional
    /// <see cref="Pause"/>/<see cref="Resume"/> → <see cref="Stop"/> or <see cref="Export"/>.
    /// <see cref="OnAutoHalt"/> fires when a timer or sample-count stop policy is satisfied.</para>
    /// </summary>
    internal sealed class ViewportExposureAccumulator : IGameplayExposureAccumulator, IRenderer
    {
        private readonly ICoreClientAPI _capi;
        private readonly ImageEffectsConfig _baselineEffects;
        private GpuExposureAccumulator? _buffer;
        private PlateProcessProfile _process;
        private float _elapsedSinceLastSample;
        private float _elapsedSinceLastPreview;
        private float _elapsedCaptureSeconds;
        private bool _rendererRegistered;
        private bool _disposed;

        /// <summary>Fired when an auto-halt policy transitions the accumulator from <see cref="ExposureState.Capturing"/> to <see cref="ExposureState.Done"/>.</summary>
        internal Action? OnAutoHalt { get; set; }

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
            _baselineEffects = ImageEffectsPipelineBridge.LoadCaptureBaseline(capi);
        }

        /// <summary>
        /// Allocates the GPU accumulator and registers this renderer at <c>AfterBlit</c> ahead of
        /// shutter press, pre-compiling GLSL programs and allocating RGBA32F ping-pong textures so
        /// there is no first-frame GPU resource stall when the player opens the shutter.
        /// </summary>
        internal void Prime()
        {
            if (_disposed) return;
            int w = Math.Max(1, _capi.Render.FrameWidth);
            int h = Math.Max(1, _capi.Render.FrameHeight);
            EnsureGpuAccumulator(w, h, 1);
            RegisterRenderer();
        }

        /// <summary>
        /// Starts a fresh accumulation session with the given chemistry and stop policy.
        /// If already <see cref="ExposureState.Paused"/>, resumes instead. No-op when already capturing.
        /// </summary>
        internal void Start(PlateProcessProfile process)
        {
            if (_disposed) return;
            if (State == ExposureState.Paused) { Resume(); return; }
            if (State == ExposureState.Capturing) return;

            _process = process;
            _elapsedSinceLastSample = 0f;
            _elapsedSinceLastPreview = 0f;
            _elapsedCaptureSeconds = 0f;

            int w = Math.Max(1, _capi.Render.FrameWidth);
            int h = Math.Max(1, _capi.Render.FrameHeight);
            EnsureGpuAccumulator(w, h, process.SampleCount);
            ApplyProcessToBuffer(_buffer!, process);
            _buffer!.Reset(); // clear any frames accumulated during priming

            // Register renderer only if Prime() hasn’t already done so during viewfinder aiming.
            if (!_rendererRegistered) RegisterRenderer();
            State = ExposureState.Capturing;
            ViewportExposureSuppressContext.ExposureCapturing = true;
            ExposurePreviewSink?.BeginExposurePassthrough();

            // at the end of Start(), after State = ExposureState.Capturing:
            _maxFrames = CollodionConfigAccess.ResolveClientConfig(_capi)?.Viewfinder?.MaxAccumulatedFrames
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

        /// <summary>
        /// Seals the exposure: resolves the buffer, applies finishing effects, and saves a PNG.
        /// Returns the saved file name. Throws when no frames have been accumulated.
        /// </summary>
        public string Export(ImageEffectsConfig? effectsOverride = null)
        {
            if (_buffer == null || _buffer.FramesAccumulated == 0)
                throw new InvalidOperationException("ViewportExposureAccumulator: no frames accumulated.");

            int maxDim = CollodionConfigAccess.ResolveClientConfig(_capi)?.Viewfinder?.PhotoCaptureMaxDimension
                ?? ViewfinderConfig.DefaultPhotoCaptureMaxDimension;

            return ExposureSeal.ToPhoto(_buffer, maxDim, "viewport-exposure", _baselineEffects, effectsOverride);
        }

        /// <summary>
        /// Serializes the current accumulated frame sums for pause/resume and tray-seal workflows.
        /// Returns <see langword="null"/> when no frames have been accumulated.
        /// </summary>
        internal byte[]? ExportPartial()
        {
            return _buffer?.SerializeAccumulation();
        }

        /// <summary>
        /// Restores a previously serialized accumulation blob into the live buffer after <see cref="Start"/> is called.
        /// Compatible with blobs produced by either <see cref="ViewportExposureAccumulator"/> or
        /// <see cref="VirtualExposureRenderer"/> (both use <see cref="GpuExposureAccumulator"/> serialization).
        /// When the blob's dimensions do not match the current buffer the call is a no-op.
        /// </summary>
        internal void PrimeFromPartial(byte[] data)
        {
            if (_buffer == null) return;

            if (!_buffer.DeserializeAccumulation(data, out int restoredFrames))
            {
                _capi.Logger.Warning("Collodion: partial exposure blob is incompatible with the current buffer dimensions — starting fresh.");
                return;
            }

            _capi.Logger.Notification($"Collodion: restored {restoredFrames} accumulated frames from saved partial exposure.");
        }

        private const float PreviewCadenceSeconds = 0.25f;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (State != ExposureState.Capturing || _buffer == null) return;

            _elapsedCaptureSeconds += deltaTime;
            _elapsedSinceLastSample += deltaTime;
            _elapsedSinceLastPreview += deltaTime;

            if (_elapsedSinceLastSample < _process.SampleInterval) return;
            _elapsedSinceLastSample -= _process.SampleInterval;

            int w = _capi.Render.FrameWidth;
            int h = _capi.Render.FrameHeight;

            // GPU blit scales into the fixed-size staging FBO, so viewport resize needs no special handling.
            _buffer.Accumulate(0, w, h);

            // Hard cap: stop regardless of stop mode once MaxAccumulatedFrames is reached.
            if (_buffer.FramesAccumulated >= _maxFrames)
            {
                CompleteAutoStop();
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
                "collodion-unregister-exposure");
            OnAutoHalt?.Invoke();
        }

        private void PushPreviewFrame()
        {
            if (_buffer == null || ExposurePreviewSink == null || _buffer.FramesAccumulated == 0) return;

            ViewfinderConfig? cfg = CollodionConfigAccess.ResolveClientConfig(_capi)?.Viewfinder;
            if (!(cfg?.DebugPreviewPeak ?? false)) return;
            int maxDimension = cfg.DebugPreviewMaxDimension;

            using SKBitmap developed = _buffer.Resolve();
            SKBitmap cropped = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(developed, maxDimension);
            try
            {
                ImageEffectsConfig? effectsOverride = ImageEffectsProfileService.TryLoadProfile("wetplate", _capi);
                ImageEffectsConfig profile = ImageEffectsPipelineBridge.ResolveCaptureProfile(_baselineEffects, effectsOverride);
                ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, "exposure-preview", profile);
                ExposurePreviewSink.StoreExposureFrame(cropped);
            }
            finally
            {
                cropped.Dispose();
            }
        }

        private static void ApplyProcessToBuffer(GpuExposureAccumulator buf, PlateProcessProfile process)
        {
            buf.RedSensitivity      = process.RedSensitivity;
            buf.GreenSensitivity    = process.GreenSensitivity;
            buf.BlueSensitivity     = process.BlueSensitivity;
            buf.DevelopmentStrength = process.DevelopmentStrength;
            buf.HDGamma             = process.HDGamma;
            buf.InertiaPoint        = process.InertiaPoint;
            buf.ReciprocityExponent = process.ReciprocityExponent;
            buf.ExposureGain        = process.ExposureGain;
        }

        private void EnsureGpuAccumulator(int sourceWidth, int sourceHeight, int sampleCount)
        {
            int maxDimension = CollodionConfigAccess.ResolveClientConfig(_capi)?.Viewfinder?.ExposureReadbackMaxDimension
                ?? ViewfinderConfig.DefaultExposureReadbackMaxDimension;
            GpuExposureAccumulator.ComputeTargetDimensions(sourceWidth, sourceHeight, maxDimension, out int w, out int h);
            if (_buffer == null || _buffer.Width != w || _buffer.Height != h)
            {
                _buffer?.Dispose();
                _buffer = new GpuExposureAccumulator(_capi, w, h, sampleCount);
            }
        }

        private void RegisterRenderer()
        {
            if (_rendererRegistered) return;
            _capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "collodion-viewport-exposure");
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
