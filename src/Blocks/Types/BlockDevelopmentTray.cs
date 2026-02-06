using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion
{
    public sealed class BlockDevelopmentTray : Block
    {
        // Liquid portion itemsPerLitre is 100 in our json.
        private const int ChemicalUnitsPerUse = 100;
        private const int DevelopPoursRequired = 5;

        private static readonly AssetLocation ExposedPlateItemCode = new AssetLocation("collodion", "exposedplate");
        private static readonly AssetLocation DevelopedPlateItemCode = new AssetLocation("collodion", "developedplate");
        private static readonly AssetLocation FinishedPlateItemCode = new AssetLocation("collodion", "finishedphotoplate");

        private static readonly AssetLocation DeveloperPortionCode = new AssetLocation("collodion", "developerportion");
        private static readonly AssetLocation FixerPortionCode = new AssetLocation("collodion", "fixerportion");
        private static readonly AssetLocation ChemicalPourSound = new AssetLocation("game:sounds/effect/water-fill");

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;

            // Client must return true to sync interaction to server.
            if (world.Side == EnumAppSide.Client)
            {
                return true;
            }

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityDevelopmentTray be)
            {
                return false;
            }

            ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;

            // Empty hand: take plate out.
            if (held == null)
            {
                if (!be.HasPlate) return false;

                ItemStack? taken = be.TakePlate();
                if (taken == null) return false;

                SwapTrayBlockForPlateStage(world, blockSel.Position, null, null);
                GiveOrDrop(world, byPlayer, taken, blockSel.Position);
                return true;
            }

            // Holding a plate: insert (only if tray empty).
            if (IsInsertablePlate(held))
            {
                if (be.HasPlate) return false;
                if (activeSlot == null) return false;

                ItemStack toInsert = held.Clone();
                toInsert.StackSize = 1;

                if (!be.TryInsertPlate(toInsert)) return false;

                string stage = IsPlate(toInsert, ExposedPlateItemCode) ? "exposed" : "developed";
                SwapTrayBlockForPlateStage(world, blockSel.Position, stage, toInsert);

                activeSlot.TakeOut(1);
                activeSlot.MarkDirty();
                return true;
            }

            // Holding developer: exposed -> developed (5 pours to finish development).
            if (IsChemicalOrContainerWith(held, DeveloperPortionCode))
            {
                if (!be.HasPlate) return false;

                ItemStack? plate = be.PlateStack;
                if (plate == null) return false;
                bool isExposed = IsPlate(plate, ExposedPlateItemCode);
                bool isDeveloped = IsPlate(plate, DevelopedPlateItemCode);
                if (!isExposed && !isDeveloped) return false;

                int currentPours = 0;
                try
                {
                    currentPours = plate.Attributes.GetInt(WetPlateAttrs.DevelopPours, isDeveloped ? DevelopPoursRequired : 0);
                }
                catch
                {
                    currentPours = isDeveloped ? DevelopPoursRequired : 0;
                }

                if (currentPours >= DevelopPoursRequired) return false;

                if (!TryConsumeChemical(activeSlot, DeveloperPortionCode))
                {
                    Tell(byPlayer, "Wetplate: need developer (at least 1 portion).", blockSel.Position);
                    return false;
                }

                ItemStack newPlate = plate;
                if (isExposed)
                {
                    Item? developedItem = world.GetItem(DevelopedPlateItemCode);
                    if (developedItem == null) return false;

                    newPlate = new ItemStack(developedItem);
                    try { newPlate.Attributes.MergeTree(plate.Attributes.Clone()); } catch { }
                }

                int newPours = currentPours + 1;
                if (newPours > DevelopPoursRequired) newPours = DevelopPoursRequired;

                newPlate.Attributes.SetInt(WetPlateAttrs.DevelopPours, newPours);
                newPlate.Attributes.SetString(WetPlateAttrs.PlateStage, newPours >= DevelopPoursRequired ? "developed" : "developing");

                be.TrySetPlate(newPlate);

                SwapTrayBlockForPlateStage(world, blockSel.Position, "developed", newPlate);
                world.PlaySoundAt(ChemicalPourSound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                return true;
            }

            // Holding fixer: developed -> finished (after 5 pours).
            if (IsChemicalOrContainerWith(held, FixerPortionCode))
            {
                if (!be.HasPlate) return false;

                ItemStack? plate = be.PlateStack;
                if (plate == null) return false;
                if (!IsPlate(plate, DevelopedPlateItemCode)) return false;

                int pours = 0;
                try
                {
                    pours = plate.Attributes.GetInt(WetPlateAttrs.DevelopPours, DevelopPoursRequired);
                }
                catch
                {
                    pours = DevelopPoursRequired;
                }

                if (pours < DevelopPoursRequired)
                {
                    Tell(byPlayer, $"Wetplate: plate not fully developed ({pours}/{DevelopPoursRequired}).", blockSel.Position);
                    return false;
                }

                if (!TryConsumeChemical(activeSlot, FixerPortionCode))
                {
                    Tell(byPlayer, "Wetplate: need fixer (at least 1 portion).", blockSel.Position);
                    return false;
                }

                Item? finishedItem = world.GetItem(FinishedPlateItemCode);
                if (finishedItem == null) return false;

                ItemStack newPlate = new ItemStack(finishedItem);
                try { newPlate.Attributes.MergeTree(plate.Attributes.Clone()); } catch { }
                newPlate.Attributes.SetString(WetPlateAttrs.PlateStage, "finished");

                be.TrySetPlate(newPlate);

                SwapTrayBlockForPlateStage(world, blockSel.Position, "finished", newPlate);
                world.PlaySoundAt(ChemicalPourSound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                return true;
            }

            return false;
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
                drops.AddRange(base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? new ItemStack[0]);
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
            if (plateToKeep != null)
            {
                if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityDevelopmentTray newBe)
                {
                    newBe.TrySetPlate(plateToKeep);
                }
            }
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

                // Develop.
                if (IsPlate(plate, ExposedPlateItemCode))
                {
                    Item? developer = world.GetItem(DeveloperPortionCode);
                    if (developer != null)
                    {
                        interactions.Add(new WorldInteraction
                        {
                            ActionLangCode = "collodion:heldhelp-developmenttray-develop",
                            MouseButton = EnumMouseButton.Right,
                            Itemstacks = new[] { new ItemStack(developer, ChemicalUnitsPerUse) }
                        });
                    }
                }

                // Fix.
                if (IsPlate(plate, DevelopedPlateItemCode))
                {
                    Item? fixer = world.GetItem(FixerPortionCode);
                    if (fixer != null)
                    {
                        interactions.Add(new WorldInteraction
                        {
                            ActionLangCode = "collodion:heldhelp-developmenttray-fix",
                            MouseButton = EnumMouseButton.Right,
                            Itemstacks = new[] { new ItemStack(fixer, ChemicalUnitsPerUse) }
                        });
                    }
                }
            }

            return interactions.ToArray();
        }

        private static bool IsChemical(ItemStack? stack, AssetLocation code)
        {
            return stack?.Collectible?.Code != null && stack.Collectible.Code == code;
        }

        private static bool IsChemicalOrContainerWith(ItemStack? stack, AssetLocation code)
        {
            if (IsChemical(stack, code)) return true;
            return HasChemicalInAttributes(stack?.Attributes, code);
        }

        private static bool HasChemicalInAttributes(ITreeAttribute? attrs, AssetLocation portionCode)
        {
            if (attrs == null) return false;

            foreach (var kvp in attrs)
            {
                IAttribute attr = kvp.Value;
                if (attr == null) continue;

                if (attr is ItemstackAttribute itemAttr)
                {
                    if (MatchesPortionCode(itemAttr.value?.Collectible?.Code, portionCode)) return true;
                }

                if (attr is TreeArrayAttribute arr && arr.value != null)
                {
                    for (int i = 0; i < arr.value.Length; i++)
                    {
                        ITreeAttribute entry = arr.value[i];
                        if (entry == null) continue;
                        if (MatchesPortionCode(entry.GetString("code", null) ?? string.Empty, portionCode)) return true;
                    }
                }

                if (attr is ITreeAttribute subtree)
                {
                    if (HasChemicalInAttributes(subtree, portionCode)) return true;
                }
            }

            return false;
        }

        private static bool TryConsumeChemical(ItemSlot? activeSlot, AssetLocation portionCode)
        {
            if (activeSlot?.Itemstack == null) return false;

            // Directly holding a portion stack.
            if (IsChemical(activeSlot.Itemstack, portionCode))
            {
                if (activeSlot.Itemstack.StackSize < ChemicalUnitsPerUse) return false;
                activeSlot.TakeOut(ChemicalUnitsPerUse);
                activeSlot.MarkDirty();
                return true;
            }

            // Holding a container (e.g., bucket/jug) with contents stored in attributes.
            // We support multiple internal layouts by scanning the attribute tree.
            if (TryConsumeChemicalFromAttributes(activeSlot.Itemstack.Attributes, portionCode, ChemicalUnitsPerUse))
            {
                activeSlot.MarkDirty();
                return true;
            }

            return false;
        }

        private static bool TryConsumeChemicalFromAttributes(ITreeAttribute? attrs, AssetLocation portionCode, int amount)
        {
            if (attrs == null) return false;

            foreach (var kvp in attrs)
            {
                IAttribute attr = kvp.Value;
                if (attr == null) continue;

                // Common runtime layout: contents stored as an ItemstackAttribute.
                if (attr is ItemstackAttribute itemAttr)
                {
                    ItemStack contained = itemAttr.value;
                    if (MatchesPortionCode(contained?.Collectible?.Code, portionCode))
                    {
                        if (contained == null || contained.StackSize < amount) return false;
                        contained.StackSize -= amount;

                        // If depleted, clear the attribute value.
                        if (contained.StackSize <= 0)
                        {
                            itemAttr.SetValue(null);
                        }

                        return true;
                    }
                }

                // JsonItemStack-style layout: array of TreeAttributes (often called ucontents).
                if (attr is TreeArrayAttribute arr && arr.value != null)
                {
                    for (int i = 0; i < arr.value.Length; i++)
                    {
                        ITreeAttribute entry = arr.value[i];
                        if (entry == null) continue;

                        string codeStr = entry.GetString("code", null) ?? string.Empty;
                        if (!MatchesPortionCode(codeStr, portionCode)) continue;

                        int stackSize = entry.GetInt("stacksize", entry.GetInt("quantity", -1));
                        if (stackSize < 0)
                        {
                            // Some stacks use makefull=true instead of stacksize.
                            if (entry.GetBool("makefull", false)) stackSize = 1000;
                        }

                        if (stackSize < amount) return false;

                        stackSize -= amount;
                        entry.SetInt("stacksize", stackSize);
                        entry.RemoveAttribute("makefull");
                        return true;
                    }
                }

                // Recurse into subtrees.
                if (attr is ITreeAttribute subtree)
                {
                    if (TryConsumeChemicalFromAttributes(subtree, portionCode, amount)) return true;
                }
            }

            return false;
        }

        private static bool MatchesPortionCode(AssetLocation? candidate, AssetLocation portionCode)
        {
            if (candidate == null) return false;

            if (candidate == portionCode) return true;

            // Accept in-container variants.
            if (candidate.Domain == portionCode.Domain && candidate.Path == $"incontainer-item-{portionCode.Path}") return true;

            // Some cases store only a path; accept same path when domain matches.
            if (candidate.Domain == portionCode.Domain && candidate.Path == portionCode.Path) return true;

            return false;
        }

        private static bool MatchesPortionCode(string candidateCodeStr, AssetLocation portionCode)
        {
            if (string.IsNullOrEmpty(candidateCodeStr)) return false;

            // Exact match.
            if (candidateCodeStr.Equals(portionCode.ToString(), System.StringComparison.OrdinalIgnoreCase)) return true;
            if (candidateCodeStr.Equals(portionCode.Path, System.StringComparison.OrdinalIgnoreCase)) return true;

            // incontainer-item-* variants.
            string incontainerPath = $"incontainer-item-{portionCode.Path}";
            if (candidateCodeStr.Equals($"{portionCode.Domain}:{incontainerPath}", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (candidateCodeStr.Equals(incontainerPath, System.StringComparison.OrdinalIgnoreCase)) return true;

            return false;
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
