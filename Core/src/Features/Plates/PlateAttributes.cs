using Vintagestory.API.Common;

namespace Photocore.Plates
{
    public enum PlateStage
    {
        Unknown = 0,
        Rough,
        Clean,
        Sensitizing,
        Sensitized,
        Exposing,
        ExposurePaused,
        Exposed,
        Developing,
        Developed,
        Finished
    }

    public static class PlateAttributes
    {
        private const string AttrStage = "photochemPlateStage";
        private const string AttrNameLangCode = "photochemPlateNameLangCode";
        private const string AttrChemistry = "photochemPlateChemistry";

        // Canonical chemistry tag stored on a plate. Today only collodion exists; its emulsion is
        // sensitised with an iodide salt, so "iodide" is both the chemistry tag and the
        // EmulsionProfile key the exposure layer resolves it to. Future processes add their own.
        public const string ChemistryCollodion = "iodide";

        public static string ToAttributeString(PlateStage stage) => stage switch
        {
            PlateStage.Rough       => "rough",
            PlateStage.Clean       => "clean",
            PlateStage.Sensitizing => "sensitizing",
            PlateStage.Sensitized    => "sensitized",
            PlateStage.Exposing      => "exposing",
            PlateStage.ExposurePaused=> "exposurepause",
            PlateStage.Exposed       => "exposed",
            PlateStage.Developing  => "developing",
            PlateStage.Developed   => "developed",
            PlateStage.Finished    => "finished",
            _                      => string.Empty
        };

        public static PlateStage FromAttributeString(string? value) => value switch
        {
            "rough"      => PlateStage.Rough,
            "clean"      => PlateStage.Clean,
            "sensitizing"=> PlateStage.Sensitizing,
            "sensitized"    => PlateStage.Sensitized,
            "exposing"      => PlateStage.Exposing,
            "exposurepause" => PlateStage.ExposurePaused,
            "exposed"       => PlateStage.Exposed,
            "developing" => PlateStage.Developing,
            "developed"  => PlateStage.Developed,
            "finished"   => PlateStage.Finished,
            _            => PlateStage.Unknown
        };

        public static void SetNameLangCode(ItemStack stack, string? nameLangCode)
        {
            if (string.IsNullOrWhiteSpace(nameLangCode))
            {
                stack.Attributes.RemoveAttribute(AttrNameLangCode);
                return;
            }

            stack.Attributes.SetString(AttrNameLangCode, nameLangCode);
        }

        public static PlateStage GetStage(ItemStack? stack)
        {
            string? raw = stack?.Attributes?.GetString(AttrStage);
            return FromAttributeString(raw);
        }

        public static PlateStage EnsureStage(ItemStack stack, PlateStage fallbackStage)
        {
            PlateStage stage = GetStage(stack);
            if (stage == PlateStage.Unknown)
            {
                SetStage(stack, fallbackStage);
                return fallbackStage;
            }

            return stage;
        }

        public static void SetStage(ItemStack stack, PlateStage stage)
        {
            stack.Attributes.SetString(AttrStage, ToAttributeString(stage));

            // A stage change invalidates any pinned display name (e.g. "Sensitized Plate" set at
            // sensitization, which otherwise survives attribute merges all the way to Finished).
            // Clearing it lets the name fall back to the stage-derived mapping in ItemPlateBase.
            // Callers that want a pinned name set it after SetStage (all current ones already do).
            stack.Attributes.RemoveAttribute(AttrNameLangCode);
        }

        // Records which photographic chemistry (emulsion/process) a plate was sensitised with.
        // Read back by the exposure layer to pick the matching EmulsionProfile. Absent on
        // legacy plates, which resolve to the collodion/iodide default.
        public static string? GetChemistry(ItemStack? stack)
            => stack?.Attributes?.GetString(AttrChemistry);

        public static void SetChemistry(ItemStack stack, string chemistryName)
        {
            if (string.IsNullOrWhiteSpace(chemistryName))
            {
                stack.Attributes.RemoveAttribute(AttrChemistry);
                return;
            }

            stack.Attributes.SetString(AttrChemistry, chemistryName);
        }

        // Lifecycle role declared by a plate itemtype's "plateRole" attribute (e.g. "sensitized",
        // "developed"). Medium-agnostic, so shared gates (camera load/exposure) work for any substrate
        // without hardcoding item codes. Null when the itemtype declares none.
        public static string? GetItemRole(ItemStack? stack)
            => stack?.Collectible?.Attributes?["plateRole"]?.AsString(null);



        // Exposure accumulation state — written when an accumulation session starts/pauses.
        public const string ExposureId           = "photochemExposureId";
        public const string ExposedFrames        = "photochemExposedFrames";
        public const string ExposureTargetFrames = "photochemExposureTargetFrames";

        // Player UID of the photographer who started this exposure. Only the original photographer may develop the plate.
        public const string PhotographerUid = "photochemPhotographerUid";

        private const string AttrPhotographerName = "photochemPhotographerName";

        // Display name of the photographer, stamped alongside PhotographerUid at shutter-open.
        // Unlike PhotographerUid (cleared once a plate is Finished, since ownership gating ends there),
        // this is never removed -- it's the "Captured by" credit and should survive to the final photo.
        public static void SetPhotographerName(ItemStack stack, string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            stack.Attributes.SetString(AttrPhotographerName, name);
        }

        public static bool TryGetPhotographerName(ItemStack? stack, out string name)
        {
            name = stack?.Attributes?.GetString(AttrPhotographerName) ?? string.Empty;
            return !string.IsNullOrEmpty(name);
        }

        private const string AttrDevelopmentStepApplications = "photochemDevelopmentStepApplications";

        public static int GetDevelopmentApplications(ItemStack? stack)
        {
            int count = stack?.Attributes?.GetInt(AttrDevelopmentStepApplications, 0) ?? 0;
            return count < 0 ? 0 : count;
        }

        public static void SetDevelopmentApplications(ItemStack stack, int applications)
        {
            stack.Attributes.SetInt(AttrDevelopmentStepApplications, applications < 0 ? 0 : applications);
        }

        public static void ResetDevelopmentApplications(ItemStack stack)
        {
            stack.Attributes.RemoveAttribute(AttrDevelopmentStepApplications);
        }

        private const string AttrReclaimCount = "photochemPlateReclaimCount";

        // Belongs to the physical glass rather than to any process stage, so unlike everything else here
        // it is never reset -- it has to survive a plate being washed down and prepared all over again.
        public static int GetReclaimCount(ItemStack? stack)
        {
            int count = stack?.Attributes?.GetInt(AttrReclaimCount, 0) ?? 0;
            return count < 0 ? 0 : count;
        }

        public static void SetReclaimCount(ItemStack stack, int count)
        {
            stack.Attributes.SetInt(AttrReclaimCount, count < 0 ? 0 : count);
        }

        private const string AttrCapturedYear  = "photochemCapturedYear";
        private const string AttrCapturedMonth = "photochemCapturedMonth";
        private const string AttrCapturedDay   = "photochemCapturedDay";

        // Stamped once at shutter-open (first Sensitized → Exposing, not a resume) and carried
        // forward untouched through develop/fix via the existing attribute MergeTree — never
        // reset or overwritten downstream. Absent on plates exposed before this existed.
        public static void SetCaptureDate(ItemStack stack, IGameCalendar calendar)
        {
            CaptureDate date = CaptureDate.From(calendar);
            stack.Attributes.SetInt(AttrCapturedYear, date.Year);
            stack.Attributes.SetInt(AttrCapturedMonth, date.Month);
            stack.Attributes.SetInt(AttrCapturedDay, date.Day);
        }

        public static bool TryGetCaptureDate(ItemStack? stack, out CaptureDate date)
        {
            if (stack?.Attributes?.HasAttribute(AttrCapturedYear) != true)
            {
                date = default;
                return false;
            }

            date = new CaptureDate(
                stack.Attributes.GetInt(AttrCapturedYear),
                stack.Attributes.GetInt(AttrCapturedMonth),
                stack.Attributes.GetInt(AttrCapturedDay));
            return true;
        }
    }
}

