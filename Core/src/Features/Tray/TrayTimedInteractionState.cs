using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
namespace Photochemistry.Tray
{
    internal static class TrayTimedInteractionState
    {
        internal const string TimedAttrKey = "photochemDevTrayTimed";
        internal const string TimedNeedReleaseKey = "photochemDevTrayNeedRelease";
        internal const string TimedActionKey = "action";
        internal const string TimedXKey = "x";
        internal const string TimedYKey = "y";
        internal const string TimedZKey = "z";
        internal const string TimedStartMsKey = "startMs";
        internal const string TimedDurationMsKey = "durationMs";

        internal static void Begin(IPlayer? byPlayer, BlockPos? pos, string action, float durationSeconds)
        {
            if (byPlayer?.Entity?.Attributes == null || pos == null) return;

            ITreeAttribute tree = byPlayer.Entity.Attributes.GetOrAddTreeAttribute(TimedAttrKey);
            tree.SetString(TimedActionKey, action);
            tree.SetInt(TimedXKey, pos.X);
            tree.SetInt(TimedYKey, pos.Y);
            tree.SetInt(TimedZKey, pos.Z);

            long nowMs = byPlayer.Entity?.World?.ElapsedMilliseconds ?? 0;
            if (nowMs <= 0) nowMs = Environment.TickCount64;
            tree.SetLong(TimedStartMsKey, nowMs);

            int durationMs = (int)Math.Round(durationSeconds * 1000f);
            if (durationMs < 1) durationMs = 1;
            tree.SetInt(TimedDurationMsKey, durationMs);
        }

        internal static bool IsActive(IPlayer? byPlayer, BlockPos? pos, string action)
        {
            if (byPlayer?.Entity?.Attributes == null || pos == null) return false;

            ITreeAttribute? tree = byPlayer.Entity.Attributes.GetTreeAttribute(TimedAttrKey);
            if (tree == null) return false;
            if (!tree.GetString(TimedActionKey, string.Empty).Equals(action, StringComparison.Ordinal)) return false;

            return tree.GetInt(TimedXKey) == pos.X && tree.GetInt(TimedYKey) == pos.Y && tree.GetInt(TimedZKey) == pos.Z;
        }

        internal static void Clear(IPlayer? byPlayer)
        {
            if (byPlayer?.Entity?.Attributes == null) return;
            byPlayer.Entity.Attributes.RemoveAttribute(TimedAttrKey);
        }

        internal static bool NeedsRelease(IPlayer? byPlayer)
            => byPlayer?.Entity?.Attributes?.GetInt(TimedNeedReleaseKey, 0) != 0;

        internal static void SetNeedsRelease(IPlayer? byPlayer)
            => byPlayer?.Entity?.Attributes?.SetInt(TimedNeedReleaseKey, 1);

        internal static void ClearNeedsRelease(IPlayer? byPlayer)
            => byPlayer?.Entity?.Attributes?.RemoveAttribute(TimedNeedReleaseKey);
    }
}
