namespace Photocore.Configuration
{
    public sealed class PhotoSyncConfig
    {
        /// <summary>How often the client sends a "photo seen" ping to the server for the currently
        /// viewed photo (keeps the server's last-seen index fresh for /photoadmin). 0 disables pings;
        /// any other value is floored at 10s so pings can't be configured into constant network chatter.</summary>
        public int PhotoSeenPingIntervalSeconds = 300;

        /// <summary>Grace period, in real-world hours, before a source photo becomes eligible for /photoadmin
        /// age/count deletion. Photos whose first-seen (or file mtime, for never-seen files) is younger than
        /// this are never auto-selected, so a freshly-taken photo that no client has rendered yet is protected.
        /// Explicit "delete id" selection bypasses this. Default 24h.</summary>
        public double PhotoDeleteGraceHours = 24.0;

        /// <summary>Network-tuning internals -- chunk/transfer sizing, cleanup intervals, per-player upload
        /// limits. Most servers never need to touch these; kept separate from the settings above so the
        /// difference is obvious at a glance.</summary>
        public PhotoSyncAdvancedConfig Advanced = new();

        internal void ClampInPlace()
        {
            PhotoSeenPingIntervalSeconds = PhotoSeenPingIntervalSeconds <= 0
                ? 0
                : Math.Clamp(PhotoSeenPingIntervalSeconds, 10, 24 * 60 * 60);
            PhotoDeleteGraceHours = Math.Clamp(PhotoDeleteGraceHours, 0.0, 8760.0); // cap at 1 year

            Advanced ??= new PhotoSyncAdvancedConfig();
            Advanced.ClampInPlace();
        }
    }
}
