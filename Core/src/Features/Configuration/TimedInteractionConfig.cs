namespace Photocore.Configuration
{
    public sealed class TimedInteractionConfig
    {
        // Keep generic so we can add start/end hooks (sounds/particles) later.
        public float DurationSeconds = 1.25f;

        internal void ClampInPlace()
        {
            DurationSeconds = Math.Clamp(DurationSeconds, 0.05f, 30f);
        }
    }
}
