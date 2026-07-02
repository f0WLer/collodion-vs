using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Photocore.Exposure;
using Photocore.ImageEffects;
using Photocore.Configuration;

namespace Photocore.CameraCapture
{
    /// <summary>
    /// Value-logic shared by the two accumulation-based exposure paths
    /// (<see cref="VirtualExposureRenderer"/> and <see cref="ViewportExposureAccumulator"/>).
    /// These operations have no lifecycle/state-machine entanglement — they only read a buffer and
    /// publish/restore — so they live here once instead of being mirrored in both renderers.
    /// </summary>
    internal static class ExposureFrameOps
    {
        internal static void PublishDevelopedPreview(
            ICoreClientAPI capi,
            GpuExposureAccumulator? buffer,
            IExposurePreviewSink? sink,
            bool applyFinishing,
            ImageEffectsConfig effects)
        {
            if (buffer == null || sink == null || buffer.FramesAccumulated == 0) return;

            ViewfinderConfig? cfg = PhotocoreConfigAccess.ResolveClientConfig(capi)?.Viewfinder;
            // Skip the GPU→CPU resolve when no one is displaying the preview (DebugPreviewPeak off).
            if (!(cfg?.DebugPreviewPeak ?? false)) return;

            using SKBitmap developed = buffer.Resolve();
            SKBitmap cropped = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(developed, cfg.DebugPreviewMaxDimension);
            try
            {
                if (applyFinishing)
                    EffectsPipeline.ApplyInPlace(cropped, "exposure-preview", effects);
                sink.StoreExposureFrame(cropped);
            }
            finally
            {
                cropped.Dispose();
            }
        }

        internal static void RestorePartial(GpuExposureAccumulator? buffer, ILogger logger, byte[] data)
        {
            if (buffer == null) return;

            if (!buffer.DeserializeAccumulation(data, out int restoredFrames))
            {
                logger.Warning("photocore: partial exposure blob is incompatible with the current buffer dimensions — starting fresh.");
                return;
            }

            logger.Notification($"photocore: restored {restoredFrames} accumulated frames from saved partial exposure.");
        }
    }
}
