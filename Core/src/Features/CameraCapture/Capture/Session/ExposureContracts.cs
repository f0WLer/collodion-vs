using SkiaSharp;
using Collodion.ImageEffects;
using Vintagestory.API.Common;

namespace Collodion.CameraCapture
{
    /// <summary>Lifecycle state of an exposure session on either the viewport or virtual-camera renderer path.</summary>
    internal enum ExposureState { Idle, Capturing, Paused, Faulted, Done }

    /// <summary>What an <see cref="IExposureStopCondition"/> wants to happen after the latest accumulated sample.</summary>
    internal enum ExposureStopAction
    {
        /// <summary>Keep accumulating.</summary>
        Continue,
        /// <summary>Auto-pause the exposure (resumable), without sealing — e.g. a timed-shutter burst elapsed.</summary>
        AutoPause,
        /// <summary>Auto-seal the exposure to a finished photo — e.g. the hard cap or target sample count was reached.</summary>
        AutoSeal
    }

    /// <summary>
    /// Pluggable shutter stop policy consulted once per accumulated sample. Baseline collodion installs no
    /// condition; the accumulator then falls back to its hard-cap auto-seal (today's manual-shutter behavior).
    /// Heads (e.g. kosphotograph) supply concrete conditions for timed / automatic shutters via
    /// <see cref="ShutterSeam.PolicyProvider"/>.
    /// </summary>
    internal interface IExposureStopCondition
    {
        /// <summary>Decides what to do after the latest sample. Consulted once per accumulated frame.</summary>
        ExposureStopAction Evaluate(int framesAccumulated, float elapsedCaptureSecondsSinceResume,
                                    int maxFrames, int targetFrames);

        /// <summary>Resets any per-burst state when a paused exposure resumes.</summary>
        void OnResumed();

        /// <summary>Gates the resume path (e.g. an automatic shutter refuses to resume at/above the target).</summary>
        bool CanResume(int framesAccumulated, int targetFrames);
    }

    /// <summary>Head-supplied factory that maps a camera item stack to its shutter stop policy.</summary>
    internal interface IShutterPolicyProvider
    {
        /// <summary>Returns the stop condition for the given camera, or <see langword="null"/> for the default manual shutter.</summary>
        IExposureStopCondition? Resolve(ItemStack cameraStack, int targetFrames);
    }

    /// <summary>Head-supplied opener for a camera's shutter-configuration UI (e.g. the timed-shutter duration dialog).</summary>
    internal interface IShutterConfigUi
    {
        /// <summary>Opens the config UI for the camera in <paramref name="cameraSlot"/>; returns <see langword="true"/> if it handled the request.</summary>
        bool TryOpenFor(ItemSlot cameraSlot);
    }

    /// <summary>
    /// Neutral install points a head uses to plug in alternate shutter behavior. Both fields are
    /// <see langword="null"/> on baseline collodion, so every consult site falls back to today's manual path.
    /// </summary>
    internal static class ShutterSeam
    {
        internal static IShutterPolicyProvider? PolicyProvider;
        internal static IShutterConfigUi? ConfigUi;
    }

    /// <summary>
    /// Shared interface for the two gameplay-level accumulation-based exposure paths:
    /// the handheld viewport accumulator and the mounted virtual-camera renderer.
    /// Implementations collect rendered frames over time and export a developed PNG when sealed.
    /// </summary>
    internal interface IGameplayExposureAccumulator : IDisposable
    {
        /// <summary>Current lifecycle state of this exposure session.</summary>
        ExposureState State { get; }
        /// <summary><see langword="true"/> while the accumulator is actively collecting frames.</summary>
        bool IsCapturing { get; }
        /// <summary>Number of frames accumulated so far in the current session.</summary>
        int FramesAccumulated { get; }
        /// <summary>Total number of samples required for a fully-exposed plate at the active chemistry.</summary>
        int TargetFrames { get; }

        /// <summary>Suspends frame accumulation without discarding the buffer.</summary>
        void Pause();
        /// <summary>Resumes frame accumulation from a previously paused state.</summary>
        void Resume();

        /// <summary>Finalizes the session without exporting. Unregisters any renderer and transitions to <see cref="ExposureState.Done"/>.</summary>
        void Stop();

        /// <summary>
        /// Seals the exposure: resolves the buffer, applies finishing effects, and writes a PNG to the photo store.
        /// Returns the saved file name, suitable for use as <c>PhotoTakenPacket.PhotoId</c>.
        /// Throws when no frames have been accumulated.
        /// </summary>
        string Export(ImageEffectsConfig? effectsOverride = null);
    }

    /// <summary>
    /// Minimal sink contract for routing developed exposure preview frames to a display surface.
    /// Decouples <see cref="VirtualExposureRenderer"/> from any specific preview renderer implementation.
    /// </summary>
    internal interface IExposurePreviewSink
    {
        /// <summary>Called when the exposure renderer begins accumulating, so the preview surface can enter passthrough mode.</summary>
        void BeginExposurePassthrough();
        /// <summary>Called when accumulation ends, reverting the preview surface out of passthrough mode.</summary>
        void EndExposurePassthrough();
        /// <summary>Delivers a developed mid-exposure preview frame to the sink for display.</summary>
        void StoreExposureFrame(SKBitmap bitmap);
        /// <summary>Resets the idle-preview timer so the next render tick produces a fresh frame.</summary>
        void ForceRefreshNextFrame();
    }
}
