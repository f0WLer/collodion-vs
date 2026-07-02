namespace Photocore.Configuration
{
    // Network-tuning internals for photo sync, split out of PhotoSyncConfig so player/server-op-facing
    // settings aren't mixed in with values that are basically never worth changing.
    public sealed class PhotoSyncAdvancedConfig
    {
        /// <summary>Per-packet payload size for image sync. Lower = smaller packet bursts but more packets; higher = fewer packets but larger bursts.</summary>
        public int ChunkSizeBytes = 24 * 1024;

        /// <summary>Maximum allowed image transfer size for upload/download.</summary>
        public int MaxTransferBytes = 2 * 1024 * 1024;

        /// <summary>How often client prunes request/download bookkeeping state.</summary>
        public int ClientStateCleanupIntervalMs = 15_000;

        /// <summary>How long client request-dedupe entries are retained.</summary>
        public float ClientRequestRetainSeconds = 300f;

        /// <summary>Client timeout for incomplete incoming image assemblies.</summary>
        public int ClientIncomingStaleMs = 120_000;

        /// <summary>How often server checks in-progress uploads for stale assemblies.</summary>
        public int ServerPruneIntervalMs = 30_000;

        /// <summary>Server timeout for incomplete upload assemblies.</summary>
        public int ServerUploadStaleMs = 120_000;

        /// <summary>Maximum concurrent in-flight uploads accepted from one player. Excess uploads are dropped.</summary>
        public int ServerMaxOpenUploadsPerPlayer = 2;

        internal void ClampInPlace()
        {
            ChunkSizeBytes = Math.Clamp(ChunkSizeBytes, 1024, 256 * 1024);
            MaxTransferBytes = Math.Clamp(MaxTransferBytes, 16 * 1024, 32 * 1024 * 1024);
            ClientStateCleanupIntervalMs = Math.Clamp(ClientStateCleanupIntervalMs, 250, 10 * 60 * 1000);
            ClientRequestRetainSeconds = Math.Clamp(ClientRequestRetainSeconds, 0f, 24f * 60f * 60f);
            ClientIncomingStaleMs = Math.Clamp(ClientIncomingStaleMs, 1000, 30 * 60 * 1000);
            ServerPruneIntervalMs = Math.Clamp(ServerPruneIntervalMs, 250, 10 * 60 * 1000);
            ServerUploadStaleMs = Math.Clamp(ServerUploadStaleMs, 1000, 30 * 60 * 1000);
            ServerMaxOpenUploadsPerPlayer = Math.Clamp(ServerMaxOpenUploadsPerPlayer, 1, 32);
        }
    }
}
