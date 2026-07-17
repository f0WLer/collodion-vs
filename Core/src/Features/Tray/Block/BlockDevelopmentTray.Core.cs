using Photocore.Configuration;
using Photocore.Plates;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
namespace Photocore.Tray
{
    internal enum TrayActionKind
    {
        Developer,
        Fixer,
        Water
    }

    public sealed partial class BlockDevelopmentTray : Block
    {
        internal const string ActionDeveloper = "developer";
        internal const string ActionFixer = "fixer";
        internal const string ActionWater = "water";
        internal const int RequiredDeveloperPours = 5;
        private const float DefaultPourSeconds = 1.25f;

        private PhotocoreConfig? Cfg => PhotocoreConfigAccess.ResolveConfig(api);
        private int GetChemicalUnitsPerUse() => Cfg?.PlateProcessing?.DevelopmentTrayChemicalUnitsPerUse ?? 40;

        private static readonly AssetLocation _photoPlateItemCode = new("photocore", "photoplate");

        private static readonly AssetLocation _developerPortionCode = new("photocore", "developerportion");
        private static readonly AssetLocation _fixerPortionCode = new("photocore", "fixerportion");
        private static readonly AssetLocation _waterPortionCode = new("game", "waterportion");

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            bool placed = base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
            if (!placed) return false;

            if (world == null || blockSel?.Position == null) return true;

            BlockPos placedPos = ResolvePlacedPos(world, blockSel);
            if (world.BlockAccessor.GetBlockEntity(placedPos) is BlockEntityDevelopmentTray be)
            {
                BlockFacing playerFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.Pos?.Yaw ?? 0f);
                be.SetPlacementFacing(playerFacing.Code, markBlockDirty: true);
            }

            return true;
        }

        private BlockPos ResolvePlacedPos(IWorldAccessor world, BlockSelection blockSel)
        {
            BlockPos selectedPos = blockSel.Position;
            Block selectedBlock = world.BlockAccessor.GetBlock(selectedPos);
            if (selectedBlock != null && selectedBlock.IsReplacableBy(this))
            {
                return selectedPos;
            }

            return selectedPos.AddCopy(blockSel.Face);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;
            return HandleTrayObjectInteractionStart(world, byPlayer, blockSel);
        }

        // Drops the base tray block plus any inserted plate so stage-specific tray block variants do not leak into inventory.
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            // Always drop the base tray block (red/blue/fire), not a loaded variant.
            var drops = new List<ItemStack>();

            Block? baseTray = GetBaseTrayBlock(world);
            if (baseTray != null)
            {
                drops.Add(new ItemStack(baseTray));
            }
            else
            {
                drops.AddRange(base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? Array.Empty<ItemStack>());
            }

            if (world?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityDevelopmentTray be && be.PlateStack != null)
            {
                drops.Add(be.PlateStack.Clone());
            }

            return drops.ToArray();
        }

        private Block? GetBaseTrayBlock(IWorldAccessor world)
        {
            if (world == null || Code == null) return null;

            string path = Code.Path;
            if (!path.StartsWith("developmenttray-")) return null;

            // path is one of:
            // developmenttray-red
            // developmenttray-red-exposed/developed/finished
            string rest = path.Substring("developmenttray-".Length);
            int dash = rest.IndexOf('-');
            string clay = dash >= 0 ? rest.Substring(0, dash) : rest;

            AssetLocation baseLoc = new(Code.Domain, $"developmenttray-{clay}");
            return world.GetBlock(baseLoc);
        }

        // Per-action pour duration. Values are already clamped by TimedInteractionConfig.ClampInPlace() on load.
        private static float PourSeconds(TimedInteractionConfig? c) => c?.DurationSeconds ?? DefaultPourSeconds;

        private float GetDeveloperPourSeconds() => PourSeconds(Cfg?.DevelopmentTrayInteractions?.Developer);
        private float GetFixerPourSeconds()     => PourSeconds(Cfg?.DevelopmentTrayInteractions?.Fixer);
        private float GetWaterPourSeconds()     => PourSeconds(Cfg?.DevelopmentTrayInteractions?.Water);

        // A plate still holding an exposure that is someone's, before developing seals it into a photo.
        // Named so the ownership rule in CanReclaimOthersExposure states which band it means instead of
        // re-listing the two stages.
        internal static bool IsPreDevelopmentExposure(PlateStage stage)
            => stage == PlateStage.Exposed || stage == PlateStage.ExposurePaused;

        // Stages reclaimable on demand rather than only once dried out. Drives both eligibility here and
        // the flat cost doubling below from one list, so a stage added to one and missed in the other
        // can't quietly diverge.
        internal static bool IsOnDemandReclaimable(PlateStage stage)
            => IsPreDevelopmentExposure(stage) || stage == PlateStage.Developing || stage == PlateStage.Developed || stage == PlateStage.Finished;

        private int GetReclaimUnits(PlateStage stage)
            => IsOnDemandReclaimable(stage) ? GetChemicalUnitsPerUse() * 2 : GetChemicalUnitsPerUse();

        private float GetReclaimSeconds(PlateStage stage)
            => IsOnDemandReclaimable(stage) ? GetWaterPourSeconds() * 2 : GetWaterPourSeconds();

        private int GetRequiredUnits(TrayActionKind actionKind, BlockEntityDevelopmentTray? be)
            => actionKind == TrayActionKind.Water
                ? GetReclaimUnits(PlateAttributes.GetStage(be?.PlateStack))
                : GetChemicalUnitsPerUse();
    }
}
