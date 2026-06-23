using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Photochemistry.AdminTooling;
using Photochemistry.Exposure;
using Photochemistry.ImageEffects;

namespace Photochemistry.CameraCapture
{
    /// <summary>
    /// Value-logic shared by the two accumulation-based exposure paths
    /// (<see cref="VirtualExposureRenderer"/> and <see cref="ViewportExposureAccumulator"/>).
    /// These operations have no lifecycle/state-machine entanglement — they only read a buffer and
    /// publish/restore — so they live here once instead of being mirrored in both renderers.
    /// </summary>
    internal static class ExposureFrameOps
    {
        // Resolves the current buffer to a developed preview frame, crops it to the plate aspect,
        // optionally applies finishing effects, and stores it in the preview sink.
        // No-op when there is nothing to show or the debug preview peek is off.
        internal static void PublishDevelopedPreview(
            ICoreClientAPI capi,
            GpuExposureAccumulator? buffer,
            IExposurePreviewSink? sink,
            bool applyFinishing,
            ImageEffectsConfig effects)
        {
            if (buffer == null || sink == null || buffer.FramesAccumulated == 0) return;

            ViewfinderConfig? cfg = PhotochemistryConfigAccess.ResolveClientConfig(capi)?.Viewfinder;
            // Skip the GPU→CPU resolve when no one is displaying the preview (DebugPreviewPeak off).
            if (!(cfg?.DebugPreviewPeak ?? false)) return;

            using SKBitmap developed = buffer.Resolve();
            SKBitmap cropped = PhotoCropMath.ScaleDownAndCenterCropToPlateAspect(developed, cfg.DebugPreviewMaxDimension);
            try
            {
                if (applyFinishing)
                    ImageEffectsPipelineBridge.ApplyCaptureEffects(cropped, "exposure-preview", effects);
                sink.StoreExposureFrame(cropped);
            }
            finally
            {
                cropped.Dispose();
            }
        }

        // Restores a previously serialized accumulation blob into the live buffer. No-op when the
        // buffer is unallocated; logs and starts fresh when the blob's dimensions are incompatible.
        internal static void RestorePartial(GpuExposureAccumulator? buffer, ILogger logger, byte[] data)
        {
            if (buffer == null) return;

            if (!buffer.DeserializeAccumulation(data, out int restoredFrames))
            {
                logger.Warning("photochemistry: partial exposure blob is incompatible with the current buffer dimensions — starting fresh.");
                return;
            }

            logger.Notification($"photochemistry: restored {restoredFrames} accumulated frames from saved partial exposure.");
        }
    }
}
