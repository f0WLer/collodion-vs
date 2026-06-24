using SkiaSharp;
using Photochemistry.ImageEffects;
using Vintagestory.API.Common;

namespace Photochemistry.CameraCapture
{
    internal enum ExposureState { Idle, Capturing, Paused, Faulted, Done }

    internal enum ExposureStopAction
    {
        Continue,
        AutoPause,
        AutoSeal
    }

    // Baseline installs no condition — the accumulator falls back to hard-cap auto-seal (manual shutter).
    // Heads supply concrete conditions for timed/automatic shutters via ShutterSeam.PolicyProvider.
    internal interface IExposureStopCondition
    {
        ExposureStopAction Evaluate(int framesAccumulated, float elapsedCaptureSecondsSinceResume,
                                    int maxFrames, int targetFrames);

        void OnResumed();

        bool CanResume(int framesAccumulated, int targetFrames);
    }

    internal interface IShutterPolicyProvider
    {
        IExposureStopCondition? Resolve(ItemStack cameraStack, int targetFrames);
    }

    internal interface IShutterConfigUi
    {
        bool TryOpenFor(ItemSlot cameraSlot);
    }

    // Both fields null on baseline — every consult site falls back to the manual shutter path.
    internal static class ShutterSeam
    {
        internal static IShutterPolicyProvider? PolicyProvider;
        internal static IShutterConfigUi? ConfigUi;
    }

    // Shared interface for the handheld viewport accumulator and the mounted virtual-camera renderer.
    internal interface IGameplayExposureAccumulator : IDisposable
    {
        ExposureState State { get; }
        bool IsCapturing { get; }
        int FramesAccumulated { get; }
        int TargetFrames { get; }

        void Pause();
        void Resume();
        void Stop();

        string Export(ImageEffectsConfig? effectsOverride = null);
    }

    // Decouples VirtualExposureRenderer from any specific preview renderer implementation.
    internal interface IExposurePreviewSink
    {
        void BeginExposurePassthrough();
        void EndExposurePassthrough();
        void StoreExposureFrame(SKBitmap bitmap);
        void ForceRefreshNextFrame();
    }
}
