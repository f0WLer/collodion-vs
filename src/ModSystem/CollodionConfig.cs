namespace Collodion
{
    public sealed class CollodionConfig
    {
        public CollodionClientConfig Client = new CollodionClientConfig();
        public WetplateEffectsConfig Effects = new WetplateEffectsConfig();
        public WetplateEffectsConfig EffectsDeveloped = CreateDevelopedEffectsDefaults();
        public PhotographConfig Photograph = new PhotographConfig();
        public PlateProcessingConfig PlateProcessing = new PlateProcessingConfig();
        public PhotoSyncConfig PhotoSync = new PhotoSyncConfig();
        public PhotoCapturePipelineConfig PhotoCapturePipeline = new PhotoCapturePipelineConfig();

        // Viewfinder capture behavior (capture runs client-side; server provides authoritative limits in multiplayer).
        public ViewfinderConfig Viewfinder = new ViewfinderConfig();

        // Timed interaction configuration (shared by client/server).
        public DevelopmentTrayInteractionConfig DevelopmentTrayInteractions = new DevelopmentTrayInteractionConfig();

        // Optional presets (editable via .collodion effects preset ...)
        public WetplateEffectsConfig EffectsPresetIndoor = new WetplateEffectsConfig();
        public WetplateEffectsConfig EffectsPresetOutdoor = new WetplateEffectsConfig();

        internal void ClampInPlace()
        {
            Client ??= new CollodionClientConfig();
            Client.ClampInPlace();

            Effects ??= new WetplateEffectsConfig();
            Effects.ClampInPlace();

            EffectsDeveloped ??= CreateDevelopedEffectsDefaults();
            EffectsDeveloped.ClampInPlace();

            Photograph ??= new PhotographConfig();
            Photograph.ClampInPlace();

            PlateProcessing ??= new PlateProcessingConfig();
            PlateProcessing.ClampInPlace();

            PhotoSync ??= new PhotoSyncConfig();
            PhotoSync.ClampInPlace();

            PhotoCapturePipeline ??= new PhotoCapturePipelineConfig();
            PhotoCapturePipeline.ClampInPlace();

            EffectsPresetIndoor ??= new WetplateEffectsConfig();
            EffectsPresetIndoor.ClampInPlace();

            EffectsPresetOutdoor ??= new WetplateEffectsConfig();
            EffectsPresetOutdoor.ClampInPlace();

            DevelopmentTrayInteractions ??= new DevelopmentTrayInteractionConfig();
            DevelopmentTrayInteractions.ClampInPlace();

            Viewfinder ??= new ViewfinderConfig();
            Viewfinder.ClampInPlace();
        }

        internal static WetplateEffectsConfig CreateDevelopedEffectsDefaults()
        {
            var cfg = new WetplateEffectsConfig();
            cfg.ClampInPlace();
            return cfg;
        }
    }

    public sealed class PhotographConfig
    {
        public string Comment_CaptionMaxLength = "Maximum caption length accepted/saved for photograph blocks. Set 0 to disable truncation.";
        public int CaptionMaxLength = 200;

        internal void ClampInPlace()
        {
            if (CaptionMaxLength < 0) CaptionMaxLength = 0;
            if (CaptionMaxLength > 5000) CaptionMaxLength = 5000;
        }
    }

    public sealed class PlateProcessingConfig
    {
        public string Comment_DevelopmentTrayChemicalUnitsPerUse = "Developer/fixer units consumed per tray pour. Lower = cheaper processing, higher = costlier.";
        public int DevelopmentTrayChemicalUnitsPerUse = 40;

        public string Comment_PolishSeconds = "Hold duration to polish rough plates. 0 = instant polish.";
        public float PolishSeconds = 2f;

        public string Comment_CoatSeconds = "Hold duration to coat clean plates with collodion. 0 = instant coating.";
        public float CoatSeconds = 2.5f;

        public string Comment_CollodionUnitsPerCoat = "Collodion units consumed for one coating action.";
        public int CollodionUnitsPerCoat = 5;

        public string Comment_ConsumePlainClothOnPolish = "If true, polishing consumes plain cloth per action.";
        public bool ConsumePlainClothOnPolish = false;

        public string Comment_PlainClothConsumedPerPolish = "Plain cloth consumed per polish when ConsumePlainClothOnPolish is true.";
        public int PlainClothConsumedPerPolish = 1;

        internal void ClampInPlace()
        {
            if (DevelopmentTrayChemicalUnitsPerUse < 1) DevelopmentTrayChemicalUnitsPerUse = 1;
            if (DevelopmentTrayChemicalUnitsPerUse > 5000) DevelopmentTrayChemicalUnitsPerUse = 5000;

            if (PolishSeconds < 0f) PolishSeconds = 0f;
            if (PolishSeconds > 30f) PolishSeconds = 30f;

            if (CoatSeconds < 0f) CoatSeconds = 0f;
            if (CoatSeconds > 30f) CoatSeconds = 30f;

            if (CollodionUnitsPerCoat < 1) CollodionUnitsPerCoat = 1;
            if (CollodionUnitsPerCoat > 5000) CollodionUnitsPerCoat = 5000;

            if (PlainClothConsumedPerPolish < 0) PlainClothConsumedPerPolish = 0;
            if (PlainClothConsumedPerPolish > 64) PlainClothConsumedPerPolish = 64;
        }
    }

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

    public sealed class PhotoCapturePipelineConfig
    {
        public string Comment_BlankDetectSampleDivisor = "Blank-frame detection sample density divisor. Lower = less CPU and fewer samples; higher = more CPU and stronger detection.";
        public int BlankDetectSampleDivisor = 32;

        public string Comment_PngCompressionQuality = "PNG compression quality parameter. Lower = faster encode/larger files; higher = slower encode/smaller files.";
        public int PngCompressionQuality = 90;

        internal void ClampInPlace()
        {
            if (BlankDetectSampleDivisor < 4) BlankDetectSampleDivisor = 4;
            if (BlankDetectSampleDivisor > 4096) BlankDetectSampleDivisor = 4096;

            if (PngCompressionQuality < 0) PngCompressionQuality = 0;
            if (PngCompressionQuality > 100) PngCompressionQuality = 100;
        }
    }

    public sealed class DevelopmentTrayInteractionConfig
    {
        public TimedInteractionConfig Developer = new TimedInteractionConfig
        {
            DurationSeconds = 1.25f
        };

        public TimedInteractionConfig Fixer = new TimedInteractionConfig
        {
            DurationSeconds = 1.25f
        };

        internal void ClampInPlace()
        {
            Developer ??= new TimedInteractionConfig { DurationSeconds = 1.25f };
            Fixer ??= new TimedInteractionConfig { DurationSeconds = 1.25f };
            Developer.ClampInPlace();
            Fixer.ClampInPlace();
        }
    }

    public sealed class TimedInteractionConfig
    {
        // Keep generic so we can add start/end hooks (sounds/particles) later.
        public float DurationSeconds = 1.25f;

        internal void ClampInPlace()
        {
            if (DurationSeconds < 0.05f) DurationSeconds = 0.05f;
            if (DurationSeconds > 30f) DurationSeconds = 30f;
        }
    }

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
