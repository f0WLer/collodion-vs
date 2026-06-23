using Photochemistry.Plates;
using Vintagestory.API.Common;

namespace Photochemistry.Exposure
{
    /// <summary>
    /// The loaded, gap-filled set of per-chemistry profiles, shared client-side by the exposure, effects, and
    /// rendering handlers so none of them hardcode a per-process value. Loaded from chemistry-profiles.json,
    /// completed by <see cref="ChemistryProfileSeeder"/> for the head's registered chemistries, and written
    /// back so the file is always whole. Lazily initialised so any handler can reach it via <see cref="Instance"/>.
    /// </summary>
    internal sealed class ChemistryProfileRegistry
    {
        private static ChemistryProfileRegistry? _instance;
        private readonly Dictionary<string, ChemistryProfile> _profiles;

        private ChemistryProfileRegistry(Dictionary<string, ChemistryProfile> profiles) => _profiles = profiles;

        /// <summary>The active registry, loading + seeding on first access if a lifecycle hook hasn't already.</summary>
        internal static ChemistryProfileRegistry Instance => _instance ??= LoadAndSeed(null);

        /// <summary>Loads, completes, and installs the registry. Call from a client lifecycle hook (with a logger)
        /// so the file is created on first run; idempotent thereafter.</summary>
        internal static ChemistryProfileRegistry LoadAndSeed(ILogger? log)
        {
            Dictionary<string, ChemistryProfile> profiles = ChemistryProfileStore.Load(log);
            ClampAll(profiles);
            bool changed = ChemistryProfileSeeder.EnsureComplete(profiles, SensitizationRegistry.RegisteredChemistries());
            if (changed) ChemistryProfileStore.Save(profiles, log);
            _instance = new ChemistryProfileRegistry(profiles);
            return _instance;
        }

        /// <summary>Clears the installed instance (client teardown).</summary>
        internal static void Clear() => _instance = null;

        /// <summary>The profile for a chemistry, creating a default (unpersisted) one for an unknown tag so callers
        /// never get null. Falls back to iodide for null/empty.</summary>
        internal ChemistryProfile Get(string? chemistry)
        {
            string key = string.IsNullOrEmpty(chemistry) ? PlateAttributes.ChemistryCollodion : chemistry.ToLowerInvariant();
            if (_profiles.TryGetValue(key, out ChemistryProfile? prof)) return prof;

            ChemistryProfile seeded = ChemistryProfileSeeder.DefaultFor(key);
            _profiles[key] = seeded;
            return seeded;
        }

        /// <summary>Persists the whole set (called after the tuner edits a profile in place).</summary>
        internal void Save(ILogger? log) => ChemistryProfileStore.Save(_profiles, log);

        private static void ClampAll(Dictionary<string, ChemistryProfile> profiles)
        {
            foreach (ChemistryProfile p in profiles.Values)
                p.PostEffects?.ClampInPlace();
        }
    }
}
