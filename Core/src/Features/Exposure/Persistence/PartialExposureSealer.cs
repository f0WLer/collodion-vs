using Vintagestory.API.Client;
using Photochemistry.ImageEffects;

namespace Photochemistry.Exposure
{
    /// <summary>
    /// Develops a saved partial accumulation blob (<c>.pex</c> file) into a finalised PNG on disk.
    /// Called when an <c>ExposurePaused</c> plate is committed to a development tray rather than
    /// being resumed in the camera. The <c>.pex</c> file is deleted only after a successful render so
    /// incompatible or corrupt partials are not destroyed during a failed tray-seal attempt.
    /// Finishing effects are applied here — accumulator paths must have <c>ApplyFinishing = false</c>.
    /// </summary>
    internal static class PartialExposureSealer
    {
        /// <summary>
        /// Loads the <c>.pex</c> for <paramref name="exposureId"/>, renders it with the given chemistry profile,
        /// target-frame normalization, output size, and effects settings, deletes the file on success, and
        /// returns the saved PNG file name.
        /// Returns <see langword="null"/> when no partial exists, the blob is corrupt, or rendering fails.
        /// </summary>
        internal static string? SealToPng(
            string exposureId,
            ICoreClientAPI capi,
            PlateProcessProfile profile,
            ExposurePhysicsConfig physics,
            int targetFrameCount,
            int maxDimension,
            ImageEffectsConfig baselineEffects,
            ImageEffectsConfig? effectsOverride = null)
        {
            if (!ExposureAccumulationStore.TryLoad(exposureId, out byte[]? data)) return null;

            string? fileName = RenderBlobToPng(data, capi, profile, physics, targetFrameCount, maxDimension, baselineEffects, effectsOverride);
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
            ImageEffectsConfig baselineEffects,
            ImageEffectsConfig? effectsOverride)
        {
            if (!ExposureAccumulationBlobFormat.TryReadHeader(data, out var header)) return null;
            if (header.FrameCount <= 0) return null;
            if (header.BackendTag != ExposureAccumulationBlobFormat.GpuBackend) return null;

            using var buffer = new GpuExposureAccumulator(capi, header.Width, header.Height, Math.Max(1, targetFrameCount));
            // Develop with the same physics flags + chemistry overrides the live exposure used, so the
            // sealed photo matches the prediction preview. A default config resolves to the process
            // profile defaults, i.e. unchanged behavior when nothing was tuned.
            physics.Apply(buffer, profile);

            if (!buffer.DeserializeAccumulation(data, out _)) return null;

            return ExposureSeal.ToPhoto(buffer, maxDimension, "plate-tray-development", baselineEffects, effectsOverride);
        }

    }
}
