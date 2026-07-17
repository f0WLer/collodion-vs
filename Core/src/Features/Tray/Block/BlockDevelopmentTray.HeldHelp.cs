using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Photocore.Plates;
namespace Photocore.Tray
{
    public sealed partial class BlockDevelopmentTray
    {
        private static readonly AssetLocation _sensitizedPlateItemCode = new("photocore", "sensitizedplate");

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return BuildWorldObjectInteractionHelp(world, selection);
        }

        private WorldInteraction[] BuildWorldObjectInteractionHelp(IWorldAccessor world, BlockSelection selection)
        {
            if (world == null || selection == null) return Array.Empty<WorldInteraction>();

            var interactions = new List<WorldInteraction>();

            BlockPos pos = selection.Position;
            if (pos == null) return Array.Empty<WorldInteraction>();

            BlockEntityDevelopmentTray? be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityDevelopmentTray;
            ItemStack? plate = be?.PlateStack;

            if (be == null || plate == null)
            {
                AddInsertPlateInteractions(world, interactions);
                return interactions.ToArray();
            }

            AddPlatePresentInteractions(world, be, plate, interactions);
            return interactions.ToArray();
        }

        private void AddInsertPlateInteractions(IWorldAccessor world, List<WorldInteraction> interactions)
        {
            Item? sensitizedItem = world.GetItem(_sensitizedPlateItemCode);
            Item? photoItem = world.GetItem(_photoPlateItemCode);

            var stacks = new List<ItemStack>();
            if (sensitizedItem != null)
            {
                ItemStack stack = new ItemStack(sensitizedItem);
                PlateAttributes.SetStage(stack, PlateStage.Exposed);
                stacks.Add(stack);
            }

            if (photoItem != null)
            {
                ItemStack stack = new ItemStack(photoItem);
                PlateAttributes.SetStage(stack, PlateStage.Developed);
                stacks.Add(stack);
            }

            if (stacks.Count <= 0) return;

            interactions.Add(new WorldInteraction
            {
                ActionLangCode = "photocore:heldhelp-developmenttray-insertplate",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = stacks.ToArray()
            });
        }

        private void AddPlatePresentInteractions(IWorldAccessor world, BlockEntityDevelopmentTray be, ItemStack plate, List<WorldInteraction> interactions)
        {
            interactions.Add(new WorldInteraction
            {
                ActionLangCode = "photocore:heldhelp-developmenttray-takeplate",
                MouseButton = EnumMouseButton.Right
            });

            bool canDevelop = TryGetDeveloperPourContext(be, out _, out _, out int currentPours)
                && currentPours < RequiredDeveloperPours;

            bool canFix = TryGetFixerPourContext(be, out _, out int pours)
                && pours >= RequiredDeveloperPours;

            bool canReclaim = TryGetReclaimContext(be, world, out _);

            if (canDevelop)
            {
                AddChemicalInteraction(world, interactions, "photocore:heldhelp-developmenttray-develop", _developerPortionCode, GetChemicalUnitsPerUse());
            }

            if (canFix)
            {
                AddChemicalInteraction(world, interactions, "photocore:heldhelp-developmenttray-fix", _fixerPortionCode, GetChemicalUnitsPerUse());
            }

            if (canReclaim)
            {
                AddChemicalInteraction(world, interactions, "photocore:heldhelp-developmenttray-reclaim", _waterPortionCode,
                    GetReclaimUnits(PlateAttributes.GetStage(plate)));
            }
        }

        private static void AddChemicalInteraction(IWorldAccessor world, List<WorldInteraction> interactions, string actionLangCode, AssetLocation itemCode, int amount)
        {
            Item? item = world.GetItem(itemCode);
            if (item == null) return;

            interactions.Add(new WorldInteraction
            {
                ActionLangCode = actionLangCode,
                MouseButton = EnumMouseButton.Right,
                Itemstacks = [new ItemStack(item, amount)]
            });
        }
    }
}
