namespace Collodion
{
    public sealed class CollodionClientConfig
    {
        // Maximum number of characters shown for Photograph caption tooltip.
        // Set to 0 or below to disable truncation.
        public string Comment_CaptionTooltipMaxLength = "Tooltip caption truncation length. Set 0 to disable tooltip truncation.";
        public int CaptionTooltipMaxLength = 180;

        // Rate-limit for sending "photo seen" pings to the server.
        // This is used to keep the server-side last-seen index updated even when
        // clients already have the image cached locally.
        // Set to 0 or below to disable pings.
        public string Comment_PhotoSeenPingIntervalSeconds = "How often client sends photo-seen ping updates. 0 disables pings.";
        public int PhotoSeenPingIntervalSeconds = 300;

        // Show the zoom mechanism notification in chat/log (e.g. Harmony patch status).
        public string Comment_ShowZoomMechanismChat = "If true, shows the zoom mechanism source/status in chat/log.";
        public bool ShowZoomMechanismChat = false;

        // Show debug/dev chat messages (e.g. mod load info, zoom mechanism tip).
        public string Comment_ShowDebugLogs = "If true, enables verbose debug/dev log messages.";
        public bool ShowDebugLogs = false;

        // Glass plate polishing
        // If enabled, consumes some amount of plain cloth per plate polished.
        public string Comment_ConsumePlainClothOnPolish = "If true, polishing rough plates consumes plain cloth.";
        public bool ConsumePlainClothOnPolish = false;

        public string Comment_PlainClothConsumedPerPolish = "Amount of plain cloth consumed per polish when consumption is enabled.";
        public int PlainClothConsumedPerPolish = 1;

        internal void ClampInPlace()
        {
            // Keep within a reasonable range; 0 or below disables truncation.
            if (CaptionTooltipMaxLength < 0) CaptionTooltipMaxLength = 0;
            if (CaptionTooltipMaxLength > 5000) CaptionTooltipMaxLength = 5000;

            if (PhotoSeenPingIntervalSeconds < 0) PhotoSeenPingIntervalSeconds = 0;
            if (PhotoSeenPingIntervalSeconds > 24 * 60 * 60) PhotoSeenPingIntervalSeconds = 24 * 60 * 60;

            if (PlainClothConsumedPerPolish < 0) PlainClothConsumedPerPolish = 0;
            if (PlainClothConsumedPerPolish > 64) PlainClothConsumedPerPolish = 64;
        }
    }
}
