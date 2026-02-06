using System.Collections.Generic;

namespace Collodion
{
    public sealed class CollodionConfig
    {
        public CollodionClientConfig Client = new CollodionClientConfig();
        public WetplateEffectsConfig Effects = new WetplateEffectsConfig();
        public WetplateEffectsConfig EffectsDeveloped = CreateDevelopedEffectsDefaults();

        // Optional presets (editable via .collodion effects preset ...)
        public WetplateEffectsConfig EffectsPresetIndoor = new WetplateEffectsConfig();
        public WetplateEffectsConfig EffectsPresetOutdoor = new WetplateEffectsConfig();

        // Live pose deltas (client-only)
        public Dictionary<string, CollodionModSystem.PoseDelta> PoseDeltas = CreateDefaultPoseDeltas();

        internal void ClampInPlace()
        {
            Client ??= new CollodionClientConfig();
            Client.ClampInPlace();

            Effects ??= new WetplateEffectsConfig();
            Effects.ClampInPlace();

            EffectsDeveloped ??= CreateDevelopedEffectsDefaults();
            EffectsDeveloped.ClampInPlace();

            EffectsPresetIndoor ??= new WetplateEffectsConfig();
            EffectsPresetIndoor.ClampInPlace();

            EffectsPresetOutdoor ??= new WetplateEffectsConfig();
            EffectsPresetOutdoor.ClampInPlace();

            PoseDeltas ??= CreateDefaultPoseDeltas();

        }

        internal static WetplateEffectsConfig CreateDevelopedEffectsDefaults()
        {
            var cfg = new WetplateEffectsConfig();
            cfg.ClampInPlace();
            return cfg;
        }

        private static Dictionary<string, CollodionModSystem.PoseDelta> CreateDefaultPoseDeltas()
        {
            return new Dictionary<string, CollodionModSystem.PoseDelta>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["gui"] = new CollodionModSystem.PoseDelta
                {
                    Tx = 0.0f, Ty = 18.0f, Tz = 0.0f,
                    Rx = 0.0f, Ry = -45.0f, Rz = 180.0f,
                    Ox = 0.0f, Oy = 0.0f, Oz = 0.0f,
                    Scale = 3.0f
                },
                ["photo-gui"] = new CollodionModSystem.PoseDelta
                {
                    Tx = 2.0f, Ty = 10.0f, Tz = 0.0f,
                    Rx = 0.0f, Ry = 0.0f, Rz = 0.0f,
                    Ox = 0.0f, Oy = 0.0f, Oz = 0.0f,
                    Scale = 2.0f
                },
                ["photo-tp"] = new CollodionModSystem.PoseDelta
                {
                    Tx = 0.0f, Ty = 0.0f, Tz = 0.0f,
                    Rx = 0.0f, Ry = 0.0f, Rz = 0.0f,
                    Ox = 0.0f, Oy = 0.0f, Oz = 0.0f,
                    Scale = 1.0f
                },
                ["tp"] = new CollodionModSystem.PoseDelta
                {
                    Tx = -1.0500001f, Ty = -0.8f, Tz = -0.35000002f,
                    Rx = 25.0f, Ry = 200.0f, Rz = 75.0f,
                    Ox = 0.5f, Oy = 0.5f, Oz = 0.5f,
                    Scale = 1.0f
                },
                ["plate"] = new CollodionModSystem.PoseDelta
                {
                    Tx = 0.0f, Ty = 0.0f, Tz = 0.0f,
                    Rx = 0.0f, Ry = 0.0f, Rz = 0.0f,
                    Ox = 0.0f, Oy = 0.0f, Oz = 0.0f,
                    Scale = 1.0f
                },
                ["fp"] = new CollodionModSystem.PoseDelta
                {
                    Tx = 0.0f, Ty = 0.0f, Tz = 0.0f,
                    Rx = 0.0f, Ry = 0.0f, Rz = 0.0f,
                    Ox = 0.0f, Oy = 0.0f, Oz = 0.0f,
                    Scale = 1.0f
                },
                ["plate-gui"] = new CollodionModSystem.PoseDelta
                {
                    Tx = 0.0f, Ty = 10.0f, Tz = 0.0f,
                    Rx = 0.0f, Ry = 45.0f, Rz = 0.0f,
                    Ox = 0.0f, Oy = 0.0f, Oz = 0.0f,
                    Scale = 2.0f
                },
                ["plate-tp"] = new CollodionModSystem.PoseDelta
                {
                    Tx = 0.0f, Ty = 0.0f, Tz = 0.0f,
                    Rx = 0.0f, Ry = 0.0f, Rz = 0.0f,
                    Ox = 0.0f, Oy = 0.0f, Oz = 0.0f,
                    Scale = 1.0f
                },
                ["plate-fp"] = new CollodionModSystem.PoseDelta
                {
                    Tx = 0.0f, Ty = 0.0f, Tz = 0.0f,
                    Rx = 0.0f, Ry = 0.0f, Rz = 0.0f,
                    Ox = 0.0f, Oy = 0.0f, Oz = 0.0f,
                    Scale = 1.0f
                },
                ["plate-s"] = new CollodionModSystem.PoseDelta
                {
                    Tx = 0.0f, Ty = 0.0f, Tz = 0.0f,
                    Rx = 0.0f, Ry = 0.0f, Rz = 0.0f,
                    Ox = 0.0f, Oy = 0.0f, Oz = 0.0f,
                    Scale = 1.0f
                }
            };
        }
    }
}
