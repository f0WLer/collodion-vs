using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Photocore.Exposure
{
    // Baseline collodion seeds iodide only — no chloride/bromide data is written into a baseline file.
    internal static class ChemistryProfileStore
    {
        private static string FilePath =>
            Path.Combine(GamePaths.DataPath, "ModData", "photocore", "chemistry-profiles.json");

        // Shared with the server-authoritative wire path (ChemistryProfileRegistry.SerializeCurrent/
        // ApplyServerProfiles) so disk and network use one round-trip format.
        internal static string Serialize(Dictionary<string, ChemistryProfile> data) =>
            JsonConvert.SerializeObject(data, Formatting.Indented);

        internal static Dictionary<string, ChemistryProfile>? Deserialize(string json)
        {
            var data = JsonConvert.DeserializeObject<Dictionary<string, ChemistryProfile>>(json);
            return data == null ? null : new Dictionary<string, ChemistryProfile>(data, StringComparer.OrdinalIgnoreCase);
        }

        internal static Dictionary<string, ChemistryProfile> Load(ILogger? log = null)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    Dictionary<string, ChemistryProfile>? data = Deserialize(File.ReadAllText(FilePath));
                    if (data != null) return data;
                }
            }
            catch (Exception ex)
            {
                log?.Warning("photocore: failed to load chemistry profiles '{0}': {1}", FilePath, ex.Message);
            }
            return new Dictionary<string, ChemistryProfile>(StringComparer.OrdinalIgnoreCase);
        }

        internal static void Save(Dictionary<string, ChemistryProfile> data, ILogger? log = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, Serialize(data));
            }
            catch (Exception ex)
            {
                log?.Warning("photocore: failed to save chemistry profiles '{0}': {1}", FilePath, ex.Message);
            }
        }
    }
}
