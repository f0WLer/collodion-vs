using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Photochemistry.Exposure
{
    // Reads/writes the unified per-chemistry profiles to a single ModData file. The file maps chemistry name
    // -> ChemistryProfile (ExposurePhysics + PostEffects + Presentation) and is the source of truth for every
    // per-process value the handlers read. ChemistryProfileSeeder fills any gaps and the file is written back,
    // so a fresh install creates it (and baseline collodion seeds iodide only — no chloride/bromide data is
    // ever written into a baseline file).
    internal static class ChemistryProfileStore
    {
        private static string FilePath =>
            Path.Combine(GamePaths.DataPath, "ModData", "photochemistry", "chemistry-profiles.json");

        internal static Dictionary<string, ChemistryProfile> Load(ILogger? log = null)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var data = JsonConvert.DeserializeObject<Dictionary<string, ChemistryProfile>>(File.ReadAllText(FilePath));
                    if (data != null) return new Dictionary<string, ChemistryProfile>(data, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                log?.Warning("photochemistry: failed to load chemistry profiles '{0}': {1}", FilePath, ex.Message);
            }
            return new Dictionary<string, ChemistryProfile>(StringComparer.OrdinalIgnoreCase);
        }

        internal static void Save(Dictionary<string, ChemistryProfile> data, ILogger? log = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                log?.Warning("photochemistry: failed to save chemistry profiles '{0}': {1}", FilePath, ex.Message);
            }
        }
    }
}
