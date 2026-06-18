namespace Photochemistry.AdminTooling
{
    // Client-only quality-of-life and debug toggles persisted in mod config.
    // Includes bounds checks so chat/tooling values stay within sane limits.
    public sealed class PhotochemistryClientConfig
    {
        /// <summary>How often client sends photo-seen ping updates. 0 disables pings.</summary>
        public int PhotoSeenPingIntervalSeconds = 300;

        /// <summary>If true, enables verbose debug/dev log messages.</summary>
        public bool ShowDebugLogs = false;

        // Clamps client-only config values so chat/JSON edits cannot push invalid ranges.
        internal void ClampInPlace()
        {
            if (PhotoSeenPingIntervalSeconds < 0) PhotoSeenPingIntervalSeconds = 0;
            if (PhotoSeenPingIntervalSeconds > 24 * 60 * 60) PhotoSeenPingIntervalSeconds = 24 * 60 * 60;
        }
    }
}
