using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Collodion
{
    public static class WetPlateAttrs
    {
        public const string WetCreatedTotalHours = "collodionWetCreatedTotalHours";
        public const string WetDurationHours = "collodionWetDurationHours";
        public const string StoredRemainingWetHours = "collodionStoredRemainingWetHours";
        public const double DefaultWetDurationHours = 0.2;
        public const string PhotoId = "photoId";
        public const string PlateStage = "collodionPlateStage";
        public const string DevelopPours = "collodionDevelopPours";
        public const string HoldStillSeconds = "collodionHoldStillSeconds";
        public const string HoldStillMovement = "collodionHoldStillMovement";

        public static double GetRemainingWetHours(IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Attributes == null) return 0;

            if (world?.Calendar == null)
            {
                return Math.Max(0, stack.Attributes.GetDouble(StoredRemainingWetHours, 0));
            }

            double created = stack.Attributes.GetDouble(WetCreatedTotalHours, -1);
            double duration = stack.Attributes.GetDouble(WetDurationHours, 0);
            if (created < 0 || duration <= 0)
            {
                return Math.Max(0, stack.Attributes.GetDouble(StoredRemainingWetHours, 0));
            }

            double elapsed = world.Calendar.TotalHours - created;
            return Math.Max(0, duration - elapsed);
        }

        public static void EnsureWetTimer(IWorldAccessor world, ItemStack stack, double durationHours)
        {
            if (world?.Calendar == null || stack?.Attributes == null) return;

            if (stack.Attributes.GetDouble(WetCreatedTotalHours, -1) < 0)
            {
                stack.Attributes.SetDouble(WetCreatedTotalHours, world.Calendar.TotalHours);
            }

            if (stack.Attributes.GetDouble(WetDurationHours, 0) <= 0)
            {
                stack.Attributes.SetDouble(WetDurationHours, durationHours);
            }
        }

        public static void AppendWetnessInfo(IWorldAccessor world, ItemStack stack, System.Text.StringBuilder dsc)
        {
            if (stack == null || dsc == null) return;

            double hoursLeft = GetRemainingWetHours(world, stack);
            if (hoursLeft > 0)
            {
                int minutesLeft = (int)Math.Ceiling(hoursLeft * 60);
                dsc.AppendLine(string.Format(Lang.Get("collodion:wetplate-wetness"), minutesLeft));
            }
            else
            {
                dsc.AppendLine(Lang.Get("collodion:wetplate-dry"));
            }
        }

        public static void PauseWetTimerForStorage(IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Attributes == null) return;

            double duration = stack.Attributes.GetDouble(WetDurationHours, 0);
            if (duration <= 0)
            {
                stack.Attributes.RemoveAttribute(StoredRemainingWetHours);
                return;
            }

            double remaining = GetRemainingWetHours(world, stack);
            if (remaining < 0) remaining = 0;

            stack.Attributes.SetDouble(StoredRemainingWetHours, remaining);
            stack.Attributes.RemoveAttribute(WetCreatedTotalHours);
            stack.Attributes.RemoveAttribute(WetDurationHours);
        }

        public static void ResumeWetTimerFromStorage(IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Attributes == null) return;

            double remaining = stack.Attributes.GetDouble(StoredRemainingWetHours, -1);
            if (remaining < 0) return;

            stack.Attributes.RemoveAttribute(StoredRemainingWetHours);

            if (remaining <= 0)
            {
                stack.Attributes.RemoveAttribute(WetCreatedTotalHours);
                stack.Attributes.RemoveAttribute(WetDurationHours);
                return;
            }

            if (world?.Calendar == null)
            {
                stack.Attributes.SetDouble(WetDurationHours, remaining);
                stack.Attributes.RemoveAttribute(WetCreatedTotalHours);
                return;
            }

            stack.Attributes.SetDouble(WetCreatedTotalHours, world.Calendar.TotalHours);
            stack.Attributes.SetDouble(WetDurationHours, remaining);
        }
    }

    public class ItemSilveredPlate : ItemPlateBase
    {
        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, GridRecipe recipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, recipe);

            if (api?.World == null) return;
            ItemStack? outStack = outputSlot?.Itemstack;
            if (outStack == null) return;

            WetPlateAttrs.EnsureWetTimer(api.World, outStack, WetPlateAttrs.DefaultWetDurationHours);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack? stack = inSlot?.Itemstack;
            if (stack == null) return;

            // Silvered plates are usually created via barrel recipes (not crafting),
            // so initialize wetness timing lazily the first time we can.
            WetPlateAttrs.EnsureWetTimer(world, stack, WetPlateAttrs.DefaultWetDurationHours);
            WetPlateAttrs.AppendWetnessInfo(world, stack, dsc);
        }
    }

    public class ItemExposedPlate : ItemPlateBase
    {
        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack? stack = inSlot?.Itemstack;
            if (stack == null) return;

            WetPlateAttrs.EnsureWetTimer(world, stack, WetPlateAttrs.DefaultWetDurationHours);
            WetPlateAttrs.AppendWetnessInfo(world, stack, dsc);
        }
    }

    public class ItemDevelopedPlate : ItemPlateBase
    {
        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack? stack = inSlot?.Itemstack;
            if (stack == null) return;

            WetPlateAttrs.EnsureWetTimer(world, stack, WetPlateAttrs.DefaultWetDurationHours);
            WetPlateAttrs.AppendWetnessInfo(world, stack, dsc);
        }
    }
}
