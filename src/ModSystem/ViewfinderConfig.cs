namespace Collodion
{
    public sealed class ViewfinderConfig
    {
        public const int MinPhotoCaptureMaxDimension = 128;
        public const int MaxPhotoCaptureMaxDimension = 2048;
        public const int DefaultPhotoCaptureMaxDimension = 640;

        public float ZoomMultiplier = 0.65f;
        public float HoldStillDurationSeconds = 4f;
        public float HoldStillLookWeight = 0.35f;
        public string Comment_HoldStillLookContributionScale = "Multiplier for look-movement contribution in hold-still scoring.";
        public float HoldStillLookContributionScale = 2f;

        public string Comment_ExposureDurationSeconds = "Timed exposure duration in seconds. 0 = instant exposure completion.";
        public float ExposureDurationSeconds = 4f;
        public int PhotoCaptureMaxDimension = DefaultPhotoCaptureMaxDimension;

        public string Comment_DebugPreviewEnabled = "If true, shows a live viewfinder debug preview window with final wetplate effects applied (client-only).";
        public bool DebugPreviewEnabled = false;
        public string Comment_DebugPreviewPeak = "If true, keeps the debug preview visible even when the viewfinder is not active (dev-only).";
        public bool DebugPreviewPeak = false;
        public string Comment_DebugPreviewRefreshMs = "Refresh interval in milliseconds for the live viewfinder debug preview (lower = more CPU/GPU use).";
        public int DebugPreviewRefreshMs = 500;
        public string Comment_DebugPreviewMaxDimension = "Max pixel size of the source capture used for the debug preview (higher = sharper but slower).";
        public int DebugPreviewMaxDimension = 384;
        public string Comment_DebugPreviewWidth = "Preview window width in screen pixels.";
        public int DebugPreviewWidth = 640;
        public string Comment_DebugPreviewHeight = "Preview window height in screen pixels.";
        public int DebugPreviewHeight = 360;
        public string Comment_DebugPreviewAnchor = "Preview anchor position: topleft | topright | bottomleft | bottomright.";
        public string DebugPreviewAnchor = "topright";
        public string Comment_DebugPreviewMargin = "Margin in pixels from the selected anchor edge.";
        public int DebugPreviewMargin = 16;

        internal void ClampInPlace()
        {
            if (ZoomMultiplier < 0.2f) ZoomMultiplier = 0.2f;
            if (ZoomMultiplier > 1f) ZoomMultiplier = 1f;

            if (HoldStillDurationSeconds < 0f) HoldStillDurationSeconds = 0f;
            if (HoldStillDurationSeconds > 30f) HoldStillDurationSeconds = 30f;

            if (HoldStillLookWeight < 0f) HoldStillLookWeight = 0f;
            if (HoldStillLookWeight > 5f) HoldStillLookWeight = 5f;

            if (HoldStillLookContributionScale < 0f) HoldStillLookContributionScale = 0f;
            if (HoldStillLookContributionScale > 20f) HoldStillLookContributionScale = 20f;

            if (ExposureDurationSeconds < 0f) ExposureDurationSeconds = 0f;
            if (ExposureDurationSeconds > 30f) ExposureDurationSeconds = 30f;

            if (PhotoCaptureMaxDimension < MinPhotoCaptureMaxDimension) PhotoCaptureMaxDimension = MinPhotoCaptureMaxDimension;
            if (PhotoCaptureMaxDimension > MaxPhotoCaptureMaxDimension) PhotoCaptureMaxDimension = MaxPhotoCaptureMaxDimension;

            if (DebugPreviewRefreshMs < 50) DebugPreviewRefreshMs = 50;
            if (DebugPreviewRefreshMs > 5000) DebugPreviewRefreshMs = 5000;

            if (DebugPreviewMaxDimension < MinPhotoCaptureMaxDimension) DebugPreviewMaxDimension = MinPhotoCaptureMaxDimension;
            if (DebugPreviewMaxDimension > MaxPhotoCaptureMaxDimension) DebugPreviewMaxDimension = MaxPhotoCaptureMaxDimension;

            if (DebugPreviewWidth < 64) DebugPreviewWidth = 64;
            if (DebugPreviewWidth > 1024) DebugPreviewWidth = 1024;

            if (DebugPreviewHeight < 64) DebugPreviewHeight = 64;
            if (DebugPreviewHeight > 1024) DebugPreviewHeight = 1024;

            if (DebugPreviewMargin < 0) DebugPreviewMargin = 0;
            if (DebugPreviewMargin > 256) DebugPreviewMargin = 256;

            DebugPreviewAnchor = (DebugPreviewAnchor ?? "topright").Trim().ToLowerInvariant();
            if (DebugPreviewAnchor != "topleft"
                && DebugPreviewAnchor != "topright"
                && DebugPreviewAnchor != "bottomleft"
                && DebugPreviewAnchor != "bottomright")
            {
                DebugPreviewAnchor = "topright";
            }
        }
    }
}
