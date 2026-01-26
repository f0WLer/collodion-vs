namespace Collodion
{
    public sealed class CollodionClientConfig
    {
        // Maximum number of characters shown for Photograph caption tooltip.
        // Set to 0 or below to disable truncation.
        public int CaptionTooltipMaxLength = 180;

        // Rate-limit for sending "photo seen" pings to the server.
        // This is used to keep the server-side last-seen index updated even when
        // clients already have the image cached locally.
        // Set to 0 or below to disable pings.
        public int PhotoSeenPingIntervalSeconds = 300;

        // Show the zoom mechanism notification in chat/log (e.g. Harmony patch status).
        public bool ShowZoomMechanismChat = false;

        // Show debug/dev chat messages (e.g. mod load info, zoom mechanism tip).
        public bool ShowDebugLogs = false;

        internal void ClampInPlace()
        {
            // Keep within a reasonable range; 0 or below disables truncation.
            if (CaptionTooltipMaxLength < 0) CaptionTooltipMaxLength = 0;
            if (CaptionTooltipMaxLength > 5000) CaptionTooltipMaxLength = 5000;

            if (PhotoSeenPingIntervalSeconds < 0) PhotoSeenPingIntervalSeconds = 0;
            if (PhotoSeenPingIntervalSeconds > 24 * 60 * 60) PhotoSeenPingIntervalSeconds = 24 * 60 * 60;
        }
    }
}
