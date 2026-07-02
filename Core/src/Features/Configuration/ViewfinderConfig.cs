namespace Photocore.Configuration
{
    // Tunables for viewfinder zoom, hold-still scoring, and debug preview behavior.
    // Values are clamped in-place to keep runtime behavior stable.
    public sealed class ViewfinderConfig
    {
        public const int MinPhotoCaptureMaxDimension = 128;
        public const int MaxPhotoCaptureMaxDimension = 2048;
        public const int DefaultPhotoCaptureMaxDimension = 640;

        public const int MinExposureReadbackMaxDimension     = 128;
        public const int MaxExposureReadbackMaxDimension     = 2048;
        public const int DefaultExposureReadbackMaxDimension = 640;

        public const int DefaultMaxAccumulatedFrames = 400;
        // 256 = double Chloride's 128-sample exposure (the highest SampleCount among EmulsionProfile
        // entries) — keeps the floor from ever locking a chemistry profile out of completing a normal
        // exposure, including profiles not yet exposed by the baseline head.
        public const int MinMaxAccumulatedFrames = 256;
        public const int MaxMaxAccumulatedFrames = 600;

        public float ZoomMultiplier = 0.65f;
        public int PhotoCaptureMaxDimension = DefaultPhotoCaptureMaxDimension;

        /// <summary>Max pixel size (longest side) of the downsampled readback buffer used during virtual exposure accumulation.
        /// Lower values reduce per-sample readback cost at the expense of slight softness in exported plates.</summary>
        public int ExposureReadbackMaxDimension = DefaultExposureReadbackMaxDimension;

        /// <summary>
        /// Max number of accumulated frames allowed during a single exposure. The accumulation buffer is a
        /// fixed Width x Height GPU target regardless of this value, so raising it doesn't cost extra memory —
        /// it only lets an exposure run longer before auto-stopping.</summary>
        public int MaxAccumulatedFrames = DefaultMaxAccumulatedFrames;

        /// <summary>If true, keeps the debug preview visible when DebugPreviewPeak mode is active (dev-only).</summary>
        public bool DebugPreviewPeak = false;
        /// <summary>Refresh interval in milliseconds for the live viewfinder debug preview (lower = more CPU/GPU use).</summary>
        public int DebugPreviewRefreshMs = 500;
        /// <summary>Max pixel size of the source capture used for the debug preview (higher = sharper but slower).</summary>
        public int DebugPreviewMaxDimension = 480;
        /// <summary>Preview window width in screen pixels.</summary>
        public int DebugPreviewWidth = 360;
        /// <summary>Preview window height in screen pixels.</summary>
        public int DebugPreviewHeight = 360;
        /// <summary>Preview anchor position: topleft | topright | bottomleft | bottomright.</summary>
        public string DebugPreviewAnchor = "topleft";
        /// <summary>Margin in pixels from the selected anchor edge.</summary>
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
