using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Photocore.Plates
{
    // Year/Month/Day are captured as already-resolved ints rather than a raw calendar timestamp
    // (e.g. TotalDays) so a later change to a world's DaysPerMonth config can't retroactively shift
    // the date a photo displays as having been taken.
    public readonly record struct CaptureDate(int Year, int Month, int Day)
    {
        public static CaptureDate From(IGameCalendar calendar) => new(
            calendar.Year,
            calendar.Month,
            // IGameCalendar has no DayOfMonth; this mirrors the engine's own GameCalendar.DayOfMonth.
            (int)(calendar.TotalDays % calendar.DaysPerMonth) + 1);

        public string ToDisplayString() =>
            Lang.Get("photocore:plate-taken-on", Lang.Get("month-" + (EnumMonth)Month), Day, Year);
    }
}
