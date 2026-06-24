using Vintagestory.API.Client;
using Photochemistry.ImageEffects;

namespace Photochemistry.Exposure
{
    // .pex is deleted only after a successful render — corrupt/incompatible partials survive a failed tray-seal attempt.
    internal static class PartialExposureSealer
    {
        internal static string? SealToPng(
            string exposureId,
            ICoreClientAPI capi,
            PlateProcessProfile profile,
            ExposurePhysicsConfig physics,
            int targetFrameCount,
            int maxDimension,
            ImageEffectsConfig effects,
            bool applyFinishing = true)
        {
            if (!ExposureAccumulationStore.TryLoad(exposureId, out byte[]? data)) return null;

            string? fileName = RenderBlobToPng(data, capi, profile, physics, targetFrameCount, maxDimension, effects, applyFinishing);
            if (!string.IsNullOrEmpty(fileName))
            {
                ExposureAccumulationStore.Delete(exposureId);
            }

            return fileName;
        }

        private static string? RenderBlobToPng(
            byte[] data,
            ICoreClientAPI capi,
            PlateProcessProfile profile,
            ExposurePhysicsConfig physics,
            int targetFrameCount,
            int maxDimension,
            ImageEffectsConfig effects,
            bool applyFinishing)
        {
            if (!ExposureAccumulationBlobFormat.TryReadHeader(data, out var header)) return null;
            if (header.FrameCount <= 0) return null;
            if (header.BackendTag != ExposureAccumulationBlobFormat.GpuBackend) return null;

            using var buffer = new GpuExposureAccumulator(capi, header.Width, header.Height, Math.Max(1, targetFrameCount));
            // Apply the same physics the live exposure used so the sealed photo matches the preview.
            physics.Apply(buffer, profile);

            if (!buffer.DeserializeAccumulation(data, out _)) return null;

            return ExposureSeal.ToPhoto(buffer, maxDimension, "plate-tray-development", effects, applyFinishing);
        }

    }
}
