using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion
{
    public sealed partial class BlockDevelopmentTray : Block
    {
        // Liquid portion itemsPerLitre is 100 in our json.
        private const int DefaultChemicalUnitsPerUse = 40;

        internal const string TimedAttrKey = "collodionDevTrayTimed";
        internal const string TimedNeedReleaseKey = "collodionDevTrayNeedRelease";
        internal const string TimedActionKey = "action";
        internal const string TimedXKey = "x";
        internal const string TimedYKey = "y";
        internal const string TimedZKey = "z";
        internal const string TimedStartMsKey = "startMs";
        internal const string TimedDurationMsKey = "durationMs";

        internal const string ActionDeveloper = "developer";
        internal const string ActionFixer = "fixer";
        internal const string ActionWater = "water";

        private CollodionConfig? Cfg => api.ModLoader.GetModSystem<CollodionModSystem>()?.Config;

        private static readonly AssetLocation SilveredPlateItemCode = new AssetLocation("collodion", "silveredplate");
        private static readonly AssetLocation ExposedPlateItemCode = new AssetLocation("collodion", "exposedplate");
        private static readonly AssetLocation DevelopedPlateItemCode = new AssetLocation("collodion", "developedplate");
        private static readonly AssetLocation FinishedPlateItemCode = new AssetLocation("collodion", "finishedphotoplate");

        private static readonly AssetLocation DeveloperPortionCode = new AssetLocation("collodion", "developerportion");
        private static readonly AssetLocation FixerPortionCode = new AssetLocation("collodion", "fixerportion");
        private static readonly AssetLocation WaterPortionCode = new AssetLocation("game", "waterportion");
        private static readonly AssetLocation RoughGlassPlateItemCode = new AssetLocation("collodion", "roughglassplate");
        private static readonly AssetLocation ChemicalPourSound = new AssetLocation("game:sounds/effect/water-fill");
        private static readonly AssetLocation FizzSound = new AssetLocation("collodion", "sounds/fizz");

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            bool placed = base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
            if (!placed) return false;

            if (world == null || blockSel?.Position == null) return true;

            BlockPos placedPos = ResolvePlacedPos(world, blockSel);
            if (world.BlockAccessor.GetBlockEntity(placedPos) is BlockEntityDevelopmentTray be)
            {
                BlockFacing playerFacing = BlockFacing.HorizontalFromYaw(byPlayer?.Entity?.SidedPos?.Yaw ?? 0f);
                be.SetPlacementFacing(playerFacing.Code, markBlockDirty: true);
            }

            return true;
        }

        private float GetDeveloperPourSeconds()
        {
            float seconds = Cfg?.DevelopmentTrayInteractions?.Developer?.DurationSeconds ?? 1.25f;
            return seconds < 0.05f ? 0.05f : seconds;
        }

        private float GetFixerPourSeconds()
        {
            float seconds = Cfg?.DevelopmentTrayInteractions?.Fixer?.DurationSeconds ?? 1.25f;
            return seconds < 0.05f ? 0.05f : seconds;
        }

        private int GetChemicalUnitsPerUse()
        {
            int amount = Cfg?.PlateProcessing?.DevelopmentTrayChemicalUnitsPerUse ?? DefaultChemicalUnitsPerUse;
            if (amount < 1) amount = 1;
            return amount;
        }

        private static void BeginTimed(IPlayer byPlayer, BlockPos pos, string action, float durationSeconds)
        {
            if (byPlayer?.Entity?.Attributes == null || pos == null) return;

            ITreeAttribute tree = byPlayer.Entity.Attributes.GetOrAddTreeAttribute(TimedAttrKey);
            tree.SetString(TimedActionKey, action);
            tree.SetInt(TimedXKey, pos.X);
            tree.SetInt(TimedYKey, pos.Y);
            tree.SetInt(TimedZKey, pos.Z);

            long nowMs = 0;
            try
            {
                nowMs = byPlayer.Entity?.World?.ElapsedMilliseconds ?? 0;
            }
            catch
            {
                nowMs = 0;
            }

            if (nowMs <= 0) nowMs = Environment.TickCount64;
            tree.SetLong(TimedStartMsKey, nowMs);

            if (durationSeconds > 0f)
            {
                int durationMs = (int)Math.Round(durationSeconds * 1000f);
                if (durationMs < 1) durationMs = 1;
                tree.SetInt(TimedDurationMsKey, durationMs);
            }
        }

        private static bool IsTimed(IPlayer byPlayer, BlockPos pos, string action)
        {
            if (byPlayer?.Entity?.Attributes == null || pos == null) return false;
            ITreeAttribute? tree = byPlayer.Entity.Attributes.GetTreeAttribute(TimedAttrKey);
            if (tree == null) return false;
            if (!tree.GetString(TimedActionKey, "").Equals(action, System.StringComparison.Ordinal)) return false;
            return tree.GetInt(TimedXKey) == pos.X && tree.GetInt(TimedYKey) == pos.Y && tree.GetInt(TimedZKey) == pos.Z;
        }

        private static void ClearTimed(IPlayer byPlayer)
        {
            if (byPlayer?.Entity?.Attributes == null) return;
            byPlayer.Entity.Attributes.RemoveAttribute(TimedAttrKey);
        }

        private static bool NeedsRelease(IPlayer byPlayer)
        {
            try
            {
                return byPlayer?.Entity?.Attributes?.GetInt(TimedNeedReleaseKey, 0) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void SetNeedsRelease(IPlayer byPlayer)
        {
            try
            {
                byPlayer?.Entity?.Attributes?.SetInt(TimedNeedReleaseKey, 1);
            }
            catch (Exception ex)
            {
                byPlayer?.Entity?.Api?.Logger?.Warning("[Collodion] SetNeedsRelease: entity attribute write failed: {0}", ex.Message);
            }
        }

        private static bool IsHoldingChemical(ItemSlot? slot, AssetLocation code)
        {
            return slot?.Itemstack != null && WetPlateChemicalUtil.IsChemicalOrContainerWith(slot.Itemstack, code);
        }

        private static int GetDevelopPours(ItemStack plate, int defaultValue)
        {
            int pours;
            try
            {
                pours = plate.Attributes.GetInt(WetPlateAttrs.DevelopPours, defaultValue);
            }
            catch
            {
                pours = defaultValue;
            }

            return pours;
        }

        private static bool TryGetDeveloperPourContext(BlockEntityDevelopmentTray be, out ItemStack plate, out bool isExposed, out bool isDeveloped, out int currentPours)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack == null)
            {
                plate = null!;
                isExposed = false;
                isDeveloped = false;
                currentPours = 0;
                return false;
            }

            plate = plateStack;

            isExposed = IsPlate(plate, ExposedPlateItemCode);
            isDeveloped = IsPlate(plate, DevelopedPlateItemCode);
            if (!isExposed && !isDeveloped)
            {
                currentPours = 0;
                return false;
            }

            currentPours = GetDevelopPours(plate, isDeveloped ? WetPlateChemicalUtil.DevelopPoursRequired : 0);
            return true;
        }

        private static bool TryGetFixerPourContext(BlockEntityDevelopmentTray be, out ItemStack plate, out int pours)
        {
            ItemStack? plateStack = be.PlateStack;
            if (plateStack == null)
            {
                plate = null!;
                pours = 0;
                return false;
            }

            plate = plateStack;

            if (!IsPlate(plate, DevelopedPlateItemCode))
            {
                pours = 0;
                return false;
            }

            pours = GetDevelopPours(plate, WetPlateChemicalUtil.DevelopPoursRequired);
            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;

            // Prevent immediately starting another timed action while RMB is still held.
            // This is enforced client-side only (server cannot observe mouse button state).
            if (world.Side == EnumAppSide.Client && NeedsRelease(byPlayer)) return false;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityDevelopmentTray be)
            {
                return false;
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;
            int chemicalUnitsPerUse = GetChemicalUnitsPerUse();

            if (world.Side == EnumAppSide.Client)
                return HandleInteractStartClient(world, byPlayer, blockSel, be, activeSlot, held, chemicalUnitsPerUse);

            return HandleInteractStartServer(world, byPlayer, blockSel, be, activeSlot, held, chemicalUnitsPerUse);
        }

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

            AssetLocation baseLoc = new AssetLocation(Code.Domain, $"developmenttray-{clay}");
            return world.GetBlock(baseLoc);
        }

        private void SwapTrayBlockForPlateStage(IWorldAccessor world, BlockPos pos, string? stage, ItemStack? plateToKeep)
        {
            if (world == null || pos == null || Code == null) return;

            string placementFacing = "east";
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityDevelopmentTray oldBe)
            {
                placementFacing = oldBe.PlacementFacingCode;
            }

            string path = Code.Path;
            if (!path.StartsWith("developmenttray-")) return;

            string rest = path.Substring("developmenttray-".Length);
            int dash = rest.IndexOf('-');
            string clay = dash >= 0 ? rest.Substring(0, dash) : rest;

            AssetLocation targetLoc = stage == null
                ? new AssetLocation(Code.Domain, $"developmenttray-{clay}")
                : new AssetLocation(Code.Domain, $"developmenttray-{clay}-{stage}");

            Block? target = world.GetBlock(targetLoc);
            if (target == null) return;

            int targetId = target.Id;
            if (targetId <= 0) return;

            world.BlockAccessor.SetBlock(targetId, pos);

            // Reapply the plate stack after swapping blocks (BE can be recreated).
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityDevelopmentTray newBe)
            {
                newBe.SetPlacementFacing(placementFacing, markBlockDirty: false);

                if (plateToKeep != null)
                {
                    newBe.TrySetPlate(plateToKeep);
                }
            }
            else if (plateToKeep != null)
            {
                world.SpawnItemEntity(plateToKeep, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
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

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            if (world == null || selection == null) return System.Array.Empty<WorldInteraction>();

            var interactions = new List<WorldInteraction>();

            BlockPos pos = selection.Position;
            if (pos == null) return System.Array.Empty<WorldInteraction>();

            BlockEntityDevelopmentTray? be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityDevelopmentTray;
            ItemStack? plate = be?.PlateStack;

            // Insert plate (exposed or developed).
            if (plate == null)
            {
                var exposedItem = world.GetItem(ExposedPlateItemCode);
                var developedItem = world.GetItem(DevelopedPlateItemCode);

                var stacks = new List<ItemStack>();
                if (exposedItem != null) stacks.Add(new ItemStack(exposedItem));
                if (developedItem != null) stacks.Add(new ItemStack(developedItem));

                if (stacks.Count > 0)
                {
                    interactions.Add(new WorldInteraction
                    {
                        ActionLangCode = "collodion:heldhelp-developmenttray-insertplate",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = stacks.ToArray()
                    });
                }
            }
            else
            {
                // Take plate.
                interactions.Add(new WorldInteraction
                {
                    ActionLangCode = "collodion:heldhelp-developmenttray-takeplate",
                    MouseButton = EnumMouseButton.Right
                });

                bool canDevelop = false;
                bool canFix = false;

                if (be != null)
                {
                    // Drive held-help by actual development progress, not just item code.
                    if (TryGetDeveloperPourContext(be, out _, out _, out _, out int currentPours))
                    {
                        canDevelop = currentPours < WetPlateChemicalUtil.DevelopPoursRequired;
                    }

                    if (TryGetFixerPourContext(be, out _, out int pours))
                    {
                        canFix = pours >= WetPlateChemicalUtil.DevelopPoursRequired;
                    }
                }
                else
                {
                    // Fallback when BE context is unavailable.
                    canDevelop = IsPlate(plate, ExposedPlateItemCode);
                    canFix = IsPlate(plate, DevelopedPlateItemCode);
                }

                // Develop / continue developing.
                if (canDevelop)
                {
                    Item? developer = world.GetItem(DeveloperPortionCode);
                    if (developer != null)
                    {
                        interactions.Add(new WorldInteraction
                        {
                            ActionLangCode = "collodion:heldhelp-developmenttray-develop",
                            MouseButton = EnumMouseButton.Right,
                            Itemstacks = new[] { new ItemStack(developer, GetChemicalUnitsPerUse()) }
                        });
                    }
                }

                // Fix.
                if (canFix)
                {
                    Item? fixer = world.GetItem(FixerPortionCode);
                    if (fixer != null)
                    {
                        interactions.Add(new WorldInteraction
                        {
                            ActionLangCode = "collodion:heldhelp-developmenttray-fix",
                            MouseButton = EnumMouseButton.Right,
                            Itemstacks = new[] { new ItemStack(fixer, GetChemicalUnitsPerUse()) }
                        });
                    }
                }
            }

            return interactions.ToArray();
        }

        private static void Tell(IPlayer byPlayer, string message, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp)
            {
                sp.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        private static bool IsInsertablePlate(ItemStack? stack)
        {
            return IsPlate(stack, ExposedPlateItemCode) || IsPlate(stack, DevelopedPlateItemCode);
        }

        private static bool IsPlate(ItemStack? stack, AssetLocation code)
        {
            return stack?.Collectible?.Code != null && stack.Collectible.Code == code;
        }

        private static void GiveOrDrop(IWorldAccessor world, IPlayer byPlayer, ItemStack stack, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp)
            {
                if (!sp.InventoryManager.TryGiveItemstack(stack))
                {
                    world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
                return;
            }

            world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}
