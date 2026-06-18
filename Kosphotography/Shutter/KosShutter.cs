using System;
using Collodion.CameraCapture;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Kosphotography
{
    // Keys and bounds for the kosphotography shutter system.
    internal static class KosCameraAttrs
    {
        // Itemtype JSON attribute on a camera: "manual" | "timed" | "automatic" (absent ⇒ manual).
        internal const string ShutterModeAttr = "shutterMode";

        internal const string ModeManual    = "manual";
        internal const string ModeTimed     = "timed";
        internal const string ModeAutomatic = "automatic";

        // Per-stack attribute: timed-shutter burst duration in seconds.
        internal const string ShutterDurationAttr = "photochemShutterDuration";

        internal const int DefaultShutterDurationSeconds = 10;
        internal const int MinShutterDurationSeconds = 1;
        internal const int MaxShutterDurationSeconds = 999; // three digits keeps dialog spacing consistent
    }

    // Timed shutter: auto-pauses (resumable) after a configurable number of capture-seconds in the current
    // burst. The accumulator resets the per-burst timer on each resume, so bursts need not be concurrent.
    // The hard cap still seals as the safety net.
    internal sealed class TimedStopCondition : IExposureStopCondition
    {
        private readonly int _durationSeconds;

        internal TimedStopCondition(int durationSeconds) => _durationSeconds = durationSeconds;

        public ExposureStopAction Evaluate(int framesAccumulated, float elapsedCaptureSecondsSinceResume,
                                           int maxFrames, int targetFrames)
        {
            if (framesAccumulated >= maxFrames) return ExposureStopAction.AutoSeal;
            if (elapsedCaptureSecondsSinceResume >= _durationSeconds) return ExposureStopAction.AutoPause;
            return ExposureStopAction.Continue;
        }

        public void OnResumed() { } // the accumulator resets its per-burst timer on resume
        public bool CanResume(int framesAccumulated, int targetFrames) => true;
    }

    // Automatic shutter: like manual, but auto-seals once the chemistry's target sample count is reached
    // (whichever of target / hard cap is smaller), and refuses to resume at or above target.
    internal sealed class TargetSampleCountStopCondition : IExposureStopCondition
    {
        public ExposureStopAction Evaluate(int framesAccumulated, float elapsedCaptureSecondsSinceResume,
                                           int maxFrames, int targetFrames)
            => framesAccumulated >= Math.Min(targetFrames, maxFrames)
                ? ExposureStopAction.AutoSeal
                : ExposureStopAction.Continue;

        public void OnResumed() { }
        public bool CanResume(int framesAccumulated, int targetFrames) => framesAccumulated < targetFrames;
    }

    // Maps a camera item stack to its shutter stop policy from the itemtype "shutterMode" attribute (and,
    // for timed, the per-stack duration). Manual ⇒ null, so Core falls back to its hard-cap auto-seal.
    internal sealed class KosShutterPolicyProvider : IShutterPolicyProvider
    {
        public IExposureStopCondition? Resolve(ItemStack cameraStack, int targetFrames)
        {
            string mode = ReadShutterMode(cameraStack);

            if (string.Equals(mode, KosCameraAttrs.ModeTimed, StringComparison.OrdinalIgnoreCase))
            {
                int duration = Math.Clamp(
                    cameraStack.Attributes.GetInt(KosCameraAttrs.ShutterDurationAttr, KosCameraAttrs.DefaultShutterDurationSeconds),
                    KosCameraAttrs.MinShutterDurationSeconds, KosCameraAttrs.MaxShutterDurationSeconds);
                return new TimedStopCondition(duration);
            }

            if (string.Equals(mode, KosCameraAttrs.ModeAutomatic, StringComparison.OrdinalIgnoreCase))
                return new TargetSampleCountStopCondition();

            return null; // manual — default behavior
        }

        internal static string ReadShutterMode(ItemStack? cameraStack)
        {
            JsonObject? attrs = cameraStack?.Collectible?.Attributes;
            if (attrs == null) return KosCameraAttrs.ModeManual;
            return attrs[KosCameraAttrs.ShutterModeAttr].AsString(KosCameraAttrs.ModeManual) ?? KosCameraAttrs.ModeManual;
        }
    }

    // Core's Ctrl+RMB hook calls this. Opens the timed-shutter duration dialog only for timed cameras;
    // returns false for manual/automatic so Core proceeds normally.
    internal sealed class KosShutterConfigUi : IShutterConfigUi
    {
        private readonly ICoreClientAPI _capi;
        private readonly KosPhotographyMod _mod;
        private GuiDialogShutterTimer? _dialog;

        internal KosShutterConfigUi(ICoreClientAPI capi, KosPhotographyMod mod)
        {
            _capi = capi;
            _mod = mod;
        }

        public bool TryOpenFor(ItemSlot cameraSlot)
        {
            ItemStack? stack = cameraSlot?.Itemstack;
            if (stack == null) return false;
            if (!string.Equals(KosShutterPolicyProvider.ReadShutterMode(stack), KosCameraAttrs.ModeTimed, StringComparison.OrdinalIgnoreCase))
                return false;

            _dialog ??= new GuiDialogShutterTimer(_capi, _mod);
            if (!_dialog.IsOpened()) _dialog.OpenForSlot(cameraSlot!);
            return true;
        }
    }
}
