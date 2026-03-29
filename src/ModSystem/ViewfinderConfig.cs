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
        }
    }
}
