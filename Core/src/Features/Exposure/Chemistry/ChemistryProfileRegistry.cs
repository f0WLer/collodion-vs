using Vintagestory.API.Common;

using Photocore.Plates;

namespace Photocore.Exposure
{
    // Lazily initialised — any handler can access Instance without a lifecycle dependency.
    internal sealed class ChemistryProfileRegistry
    {
        private static ChemistryProfileRegistry? _instance;
        private readonly Dictionary<string, ChemistryProfile> _profiles;

        private ChemistryProfileRegistry(Dictionary<string, ChemistryProfile> profiles) => _profiles = profiles;

        // Lazy — LoadAndSeed is called on first access if the lifecycle hook hasn't already done so.
        internal static ChemistryProfileRegistry Instance => _instance ??= LoadAndSeed(null);

        // Call from a lifecycle hook so the file is created on first run with a logger; idempotent thereafter.
        internal static ChemistryProfileRegistry LoadAndSeed(ILogger? log)
        {
            Dictionary<string, ChemistryProfile> profiles = ChemistryProfileStore.Load(log);
            ClampAll(profiles);
            bool changed = ChemistryProfileSeeder.EnsureComplete(profiles, SensitizationRegistry.RegisteredChemistries());
            if (changed) ChemistryProfileStore.Save(profiles, log);
            _instance = new ChemistryProfileRegistry(profiles);
            return _instance;
        }

        internal static void Clear() => _instance = null;

        // Returns a default (unpersisted) profile for unknown tags — never returns null. Falls back to iodide for null/empty.
        internal ChemistryProfile Get(string? chemistry)
        {
            string key = string.IsNullOrEmpty(chemistry) ? PlateAttributes.ChemistryCollodion : chemistry.ToLowerInvariant();
            if (_profiles.TryGetValue(key, out ChemistryProfile? prof)) return prof;

            ChemistryProfile seeded = ChemistryProfileSeeder.DefaultFor(key);
            _profiles[key] = seeded;
            return seeded;
        }

        internal void Save(ILogger? log) => ChemistryProfileStore.Save(_profiles, log);

        private static void ClampAll(Dictionary<string, ChemistryProfile> profiles)
        {
            foreach (ChemistryProfile p in profiles.Values)
                p.PostEffects?.ClampInPlace();
        }
    }
}
