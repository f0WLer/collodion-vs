using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Photochemistry.Exposure
{
    // Baseline collodion seeds iodide only — no chloride/bromide data is written into a baseline file.
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
