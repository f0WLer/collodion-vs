using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Photochemistry.Exposure
{
    // Reads/writes the per-chemistry exposure profiles to a single ModData file (mirrors
    // ImageEffectsProfileService). The file maps chemistry name -> ChemistryOverrides and is the source of
    // truth for the accumulation params the physics tuner edits: once it exists it is read at startup and
    // written back by the Save button. The renderer seeds it from the hardcoded PlateProcessProfile defaults
    // when it is missing, so a fresh install creates it (and baseline collodion seeds iodide only — no
    // chloride/bromide data is ever written into a baseline file).
    internal static class ChemistryProfileStore
    {
        private static string FilePath =>
            Path.Combine(GamePaths.DataPath, "ModData", "photochemistry", "chemistry-profiles.json");

        internal static Dictionary<string, ChemistryOverrides> Load(ILogger? log = null)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var data = JsonConvert.DeserializeObject<Dictionary<string, ChemistryOverrides>>(File.ReadAllText(FilePath));
                    if (data != null) return new Dictionary<string, ChemistryOverrides>(data, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                log?.Warning("photochemistry: failed to load chemistry profiles '{0}': {1}", FilePath, ex.Message);
            }
            return new Dictionary<string, ChemistryOverrides>(StringComparer.OrdinalIgnoreCase);
        }

        internal static void Save(Dictionary<string, ChemistryOverrides> data, ILogger? log = null)
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
