using Newtonsoft.Json;

namespace Photocore.Configuration
{
    // Tunables for viewfinder zoom, hold-still scoring, and debug preview behavior.
    // Values are clamped in-place to keep runtime behavior stable.
    public sealed class ViewfinderConfig
    {
        public const int MinPhotoCaptureMaxDimension = 128;
        // 1024 is the largest photo that stays under PhotoSyncAdvancedConfig.MaxTransferBytes (2 MB) even
        // for the grainiest captures (~1.45 bytes/px, measured across finished plates); a client silently
        // skips uploading anything larger, so the photo would never reach the server. It also keeps a photo
        // comfortably inside one texture-atlas page: atlases are capped at 4096x4096, and a 2048px photo is
        // an entire page's height on installs whose maxTextureAtlasHeight never migrated off 2048.
        public const int MaxPhotoCaptureMaxDimension = 1024;
        public const int DefaultPhotoCaptureMaxDimension = 640;

        public const int MinExposureReadbackMaxDimension     = 128;
        // Never below MaxPhotoCaptureMaxDimension: the seal only ever downscales the accumulation buffer
        // (PhotoCropMath.ScaleDownAndCenterCropToPlateAspect clamps its scale to 1), so this value is the
        // real ceiling on captured photo size and a smaller one silently caps PhotoCaptureMaxDimension.
        public const int MaxExposureReadbackMaxDimension     = 2048;
        public const int DefaultExposureReadbackMaxDimension = 640;

        public const int DefaultMaxAccumulatedFrames = 600;
        // 256 = double Chloride's 128-sample exposure (the highest SampleCount among EmulsionProfile
        // entries) — keeps the floor from ever locking a chemistry profile out of completing a normal
        // exposure, including profiles not yet exposed by the baseline head.
        public const int MinMaxAccumulatedFrames = 256;
        public const int MaxMaxAccumulatedFrames = 1000;

        // Not persisted to photocore.json: no in-game way to change it, so it's a code-level tunable only.
        [JsonIgnore]
        public float ZoomMultiplier = 0.65f;
        public int PhotoCaptureMaxDimension = DefaultPhotoCaptureMaxDimension;

        /// <summary>Max pixel size (longest side) of the downsampled readback buffer used during virtual exposure accumulation.
        /// Lower values reduce per-sample readback cost at the expense of slight softness in exported plates.</summary>
        // Not persisted to photocore.json: no in-game way to change it, so it's a code-level tunable only.
        [JsonIgnore]
        public int ExposureReadbackMaxDimension = DefaultExposureReadbackMaxDimension;

        /// <summary>The accumulation buffer's longest side. Never smaller than the photo being captured from it.</summary>
        // The seal only downscales (PhotoCropMath clamps its scale to 1), so a readback buffer smaller than
        // PhotoCaptureMaxDimension silently caps the photo at the buffer's size. Read this rather than the
        // raw field, and read it late: in multiplayer the server overwrites PhotoCaptureMaxDimension in
        // memory after the config is clamped, so a server asking for photos larger than this client's own
        // readback default would otherwise be quietly ignored.
        //
        // Above the capture size the extra resolution is supersampling — it costs GPU memory (40 bytes per
        // pixel across the two Rgba32f accumulation targets and two Rgba8 helpers) and enlarges the partial
        // exposure blobs written to disk (16 bytes per pixel), so the default leaves it equal to the capture
        // size rather than paying for detail that gets scaled away.
        [JsonIgnore]
        public int EffectiveExposureReadbackMaxDimension => Math.Clamp(
            Math.Max(ExposureReadbackMaxDimension, PhotoCaptureMaxDimension),
            MinExposureReadbackMaxDimension,
            MaxExposureReadbackMaxDimension);

        /// <summary>
        /// Max number of accumulated frames allowed during a single exposure. The accumulation buffer is a
        /// fixed Width x Height GPU target regardless of this value, so raising it doesn't cost extra memory —
        /// it only lets an exposure run longer before auto-stopping.</summary>
        public int MaxAccumulatedFrames = DefaultMaxAccumulatedFrames;

        // The 7 DebugPreview* fields below are dev-only (driven live by the ".photocore preview" chat
        // command) and are intentionally not persisted to photocore.json -- that command's changes are
        // session-only now, which is fine since it's not a player/server-op-facing feature.
        /// <summary>If true, keeps the debug preview visible when DebugPreviewPeak mode is active (dev-only).</summary>
        [JsonIgnore]
        public bool DebugPreviewPeak = false;
        /// <summary>Refresh interval in milliseconds for the live viewfinder debug preview (lower = more CPU/GPU use).</summary>
        [JsonIgnore]
        public int DebugPreviewRefreshMs = 500;
        /// <summary>Max pixel size of the source capture used for the debug preview (higher = sharper but slower).</summary>
        [JsonIgnore]
        public int DebugPreviewMaxDimension = 480;
        /// <summary>Preview window width in screen pixels.</summary>
        [JsonIgnore]
        public int DebugPreviewWidth = 360;
        /// <summary>Preview window height in screen pixels.</summary>
        [JsonIgnore]
        public int DebugPreviewHeight = 360;
        /// <summary>Preview anchor position: topleft | topright | bottomleft | bottomright.</summary>
        [JsonIgnore]
        public string DebugPreviewAnchor = "topleft";
        /// <summary>Margin in pixels from the selected anchor edge.</summary>
        [JsonIgnore]
        public int DebugPreviewMargin = 16;

        /// <summary>
        /// When true, the wet-plate drying clock is paused while the plate is in the
        /// Exposing or ExposurePaused lifecycle stage.
        /// </summary>
        public bool PauseDryingDuringExposure = true;

        /// <summary>
        /// When true, the post-exposure finishing effects (grain, vignette, halation, lens softness,
        /// coating unevenness, dust/scratches, edge toning) are baked into the final developed photo.
        /// Independent of the debug dialog's "Apply Finishing" toggle, which only controls the preview peek —
        /// so finishing can be previewed while leaving the saved plate photos clean.
        /// </summary>
        public bool ApplyFinishingEffects = true;

        // Clamps all viewfinder and preview tuning values to safe runtime ranges.
        internal void ClampInPlace()
        {
            ZoomMultiplier = Math.Clamp(ZoomMultiplier, 0.2f, 1f);
            PhotoCaptureMaxDimension = Math.Clamp(PhotoCaptureMaxDimension, MinPhotoCaptureMaxDimension, MaxPhotoCaptureMaxDimension);
            ExposureReadbackMaxDimension = Math.Clamp(ExposureReadbackMaxDimension, MinExposureReadbackMaxDimension, MaxExposureReadbackMaxDimension);
            MaxAccumulatedFrames = Math.Clamp(MaxAccumulatedFrames, MinMaxAccumulatedFrames, MaxMaxAccumulatedFrames);
            DebugPreviewRefreshMs = Math.Clamp(DebugPreviewRefreshMs, 50, 5000);
            DebugPreviewMaxDimension = Math.Clamp(DebugPreviewMaxDimension, MinPhotoCaptureMaxDimension, MaxPhotoCaptureMaxDimension);
            DebugPreviewWidth = Math.Clamp(DebugPreviewWidth, 64, 1024);
            DebugPreviewHeight = Math.Clamp(DebugPreviewHeight, 64, 1024);
            DebugPreviewMargin = Math.Clamp(DebugPreviewMargin, 0, 256);

            DebugPreviewAnchor = (DebugPreviewAnchor ?? "topleft").Trim().ToLowerInvariant();
            if (DebugPreviewAnchor != "topleft"
                && DebugPreviewAnchor != "topright"
                && DebugPreviewAnchor != "bottomleft"
                && DebugPreviewAnchor != "bottomright")
            {
                DebugPreviewAnchor = "topleft";
            }
        }
    }
}
