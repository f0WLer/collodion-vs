namespace Collodion
{
    public sealed class PhotoSyncConfig
    {
        public string Comment_ChunkSizeBytes = "Per-packet payload size for image sync. Lower = smaller packet bursts but more packets; higher = fewer packets but larger bursts.";
        public int ChunkSizeBytes = 24 * 1024;

        public string Comment_MaxTransferBytes = "Maximum allowed image transfer size for upload/download.";
        public int MaxTransferBytes = 2 * 1024 * 1024;

        public string Comment_ClientStateCleanupIntervalMs = "How often client prunes request/download bookkeeping state.";
        public int ClientStateCleanupIntervalMs = 15_000;

        public string Comment_ClientRequestRetainSeconds = "How long client request-dedupe entries are retained.";
        public float ClientRequestRetainSeconds = 300f;

        public string Comment_ClientIncomingStaleMs = "Client timeout for incomplete incoming image assemblies.";
        public int ClientIncomingStaleMs = 120_000;

        public string Comment_ServerPruneIntervalMs = "How often server checks in-progress uploads for stale assemblies.";
        public int ServerPruneIntervalMs = 30_000;

        public string Comment_ServerUploadStaleMs = "Server timeout for incomplete upload assemblies.";
        public int ServerUploadStaleMs = 120_000;

        internal void ClampInPlace()
        {
            if (ChunkSizeBytes < 1024) ChunkSizeBytes = 1024;
            if (ChunkSizeBytes > 256 * 1024) ChunkSizeBytes = 256 * 1024;

            if (MaxTransferBytes < 16 * 1024) MaxTransferBytes = 16 * 1024;
            if (MaxTransferBytes > 32 * 1024 * 1024) MaxTransferBytes = 32 * 1024 * 1024;

            if (ClientStateCleanupIntervalMs < 250) ClientStateCleanupIntervalMs = 250;
            if (ClientStateCleanupIntervalMs > 10 * 60 * 1000) ClientStateCleanupIntervalMs = 10 * 60 * 1000;

            if (ClientRequestRetainSeconds < 0f) ClientRequestRetainSeconds = 0f;
            if (ClientRequestRetainSeconds > 24f * 60f * 60f) ClientRequestRetainSeconds = 24f * 60f * 60f;

            if (ClientIncomingStaleMs < 1000) ClientIncomingStaleMs = 1000;
            if (ClientIncomingStaleMs > 30 * 60 * 1000) ClientIncomingStaleMs = 30 * 60 * 1000;

            if (ServerPruneIntervalMs < 250) ServerPruneIntervalMs = 250;
            if (ServerPruneIntervalMs > 10 * 60 * 1000) ServerPruneIntervalMs = 10 * 60 * 1000;

            if (ServerUploadStaleMs < 1000) ServerUploadStaleMs = 1000;
            if (ServerUploadStaleMs > 30 * 60 * 1000) ServerUploadStaleMs = 30 * 60 * 1000;
        }
    }
}
