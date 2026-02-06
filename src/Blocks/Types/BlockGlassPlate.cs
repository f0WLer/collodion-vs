using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Collodion
{
    public sealed class BlockGlassPlate : Block
    {
        private const float PolishSeconds = 2.0f;
        private const float CoatSeconds = 5.0f;
        // Collodion portion itemsPerLitre is 100 in json, so 1 unit = 10mL.
        private const int CollodionUnitsPerCoat = 5; // 50mL

        private CollodionModSystem? modSys;

        private static readonly AssetLocation PlainClothCode = new AssetLocation("game", "cloth-plain");
        private static readonly AssetLocation PolishSound = new AssetLocation("game:sounds/player/chalkdraw");
        private static readonly AssetLocation CollodionPourSound = new AssetLocation("game:sounds/effect/water-fill");
        private static readonly AssetLocation CollodionPortionCode = new AssetLocation("collodion", "collodionportion");

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            modSys = api.ModLoader.GetModSystem<CollodionModSystem>();

            // These blocks use per-texture alpha. If rendered in the opaque pass, they will write depth
            // and can cause "see under terrain" artifacts because terrain behind them never renders.
            RenderPass = EnumChunkRenderPass.Transparent;
        }

        private string GetPlateState()
        {
            string? variantState = Variant?["state"];
            if (!string.IsNullOrEmpty(variantState)) return variantState;

            string path = Code?.Path ?? "";
            if (path.EndsWith("-clean")) return "clean";
            if (path.EndsWith("-coated")) return "coated";
            return "rough";
        }

        private Block? GetBlockForState(IWorldAccessor world, string state)
        {
            // Blocks are registered as collodion:plate-rough/clean/coated.
            return world?.GetBlock(new AssetLocation(Code?.Domain ?? "collodion", $"plate-{state}"));
        }

        private bool TryCreatePlateItemStack(IWorldAccessor world, out ItemStack stack)
        {
            stack = default!;

            string state = GetPlateState();
            AssetLocation itemCode = state switch
            {
                "clean" => new AssetLocation("collodion", "cleanglassplate"),
                "coated" => new AssetLocation("collodion", "collodioncoatedplate"),
                _ => new AssetLocation("collodion", "roughglassplate")
            };

            Item? item = world?.GetItem(itemCode);
            if (item == null) return false;

            stack = new ItemStack(item);
            return true;
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            // Plates should drop their corresponding *item* (rough/clean/coated), not the block itself.
            if (TryCreatePlateItemStack(world, out ItemStack stack))
            {
                return new[] { stack };
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            if (TryCreatePlateItemStack(world, out ItemStack stack))
            {
                return stack;
            }

            return base.OnPickBlock(world, pos);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;

            string state = GetPlateState();
            bool isRough = state == "rough";
            bool isClean = state == "clean";

            bool isPolish = isRough && IsPolishModifierDown(byPlayer) && IsHoldingPlainCloth(byPlayer);
            bool isCoat = isClean && IsHoldingCollodion(byPlayer);
            bool isPickup = IsEmptyHand(byPlayer);

            // Client must return true to sync interaction to server.
            // Only do so when we actually handle the interaction.
            if (world.Side == EnumAppSide.Client)
            {
                return isPolish || isCoat || isPickup;
            }

            if (isPolish || isCoat)
            {
                if (world.Side == EnumAppSide.Server)
                {
                    AssetLocation sound = isPolish ? PolishSound : CollodionPourSound;
                    world.PlaySoundAt(sound, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, null);
                }

                return true;
            }

            // Pickup is only allowed with an empty hand to prevent "auto pickup" after hold interactions.
            if (isPickup)
            {
                GiveItemAndRemoveBlock(world, byPlayer, blockSel.Position);
                return true;
            }

            return false;
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world == null || byPlayer == null || blockSel == null) return false;

            string state = GetPlateState();
            bool isRough = state == "rough";

            if (isRough)
            {
                if (!IsPolishModifierDown(byPlayer) || !IsHoldingPlainCloth(byPlayer)) return false;

                if (secondsUsed < PolishSeconds)
                {
                    return true;
                }

                if (world.Side == EnumAppSide.Server)
                {
                    Block? cleanBlock = GetBlockForState(world, "clean");
                    if (cleanBlock != null)
                    {
                        bool isCreative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;

                        int consumeCount = 1;
                        if (modSys?.Config?.Client?.ConsumePlainClothOnPolish == false)
                        {
                            consumeCount = 0;
                        }
                        else if (modSys?.Config != null)
                        {
                            consumeCount = modSys.Config.Client.PlainClothConsumedPerPolish;
                        }

                        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
                        if (!isCreative && consumeCount > 0)
                        {
                            if (activeSlot?.Itemstack == null || activeSlot.Itemstack.StackSize < consumeCount)
                            {
                                return false;
                            }
                        }

                        world.BlockAccessor.SetBlock(cleanBlock.Id, blockSel.Position);
                        world.BlockAccessor.MarkBlockDirty(blockSel.Position);

                        if (!isCreative && consumeCount > 0)
                        {
                            activeSlot!.TakeOut(consumeCount);
                            activeSlot.MarkDirty();
                        }

                    }
                }

                return false;
            }

            // Collodion coating (hold RMB).
            bool isClean = state == "clean";
            if (!isClean) return false;

            if (!IsHoldingCollodion(byPlayer)) return false;

            if (secondsUsed < CoatSeconds)
            {
                return true;
            }

            if (world.Side == EnumAppSide.Server)
            {
                ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;

                bool isCreative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
                if (!isCreative && !TryConsumeCollodion(activeSlot, CollodionUnitsPerCoat))
                {
                    return false;
                }

                Block? coatedBlock = GetBlockForState(world, "coated");
                if (coatedBlock != null)
                {
                    world.BlockAccessor.SetBlock(coatedBlock.Id, blockSel.Position);
                    world.BlockAccessor.MarkBlockDirty(blockSel.Position);
                }

            }

            return false;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            string state = GetPlateState();

            if (state == "rough")
            {
                Item? clothItem = world.GetItem(PlainClothCode);
                if (clothItem == null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

                ItemStack clothStack = new ItemStack(clothItem);

                return new[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "collodion:heldhelp-cleanroughglass",
                        HotKeyCode = "sneak",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = new[] { clothStack }
                    }
                };
            }

            if (state == "clean")
            {
                Item? collodionItem = world.GetItem(CollodionPortionCode);
                if (collodionItem == null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

                ItemStack collodionStack = new ItemStack(collodionItem, CollodionUnitsPerCoat);

                return new[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "collodion:heldhelp-coatglassplate",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = new[] { collodionStack }
                    }
                };
            }

            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        private static bool IsHoldingPlainCloth(IPlayer player)
        {
            ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;
            return held?.Collectible?.Code != null && held.Collectible.Code == PlainClothCode;
        }

        private static bool IsPolishModifierDown(IPlayer player)
        {
            // Prefer ShiftKey for mouse interaction modifiers; Sneak is a movement state.
            var controls = player.Entity?.Controls;
            return controls?.ShiftKey == true || controls?.Sneak == true;
        }

        private static bool IsHoldingCollodion(IPlayer player)
        {
            ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
            ItemStack? held = activeSlot?.Itemstack;
            return held != null && IsCollodionOrContainerWith(held);
        }

        private static bool IsEmptyHand(IPlayer player)
        {
            ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
            return activeSlot?.Itemstack == null;
        }

        private void GiveItemAndRemoveBlock(IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            if (byPlayer is IServerPlayer sp && TryCreatePlateItemStack(world, out ItemStack stack))
            {
                if (!sp.InventoryManager.TryGiveItemstack(stack))
                {
                    world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
        }

        private static bool IsCollodion(ItemStack stack)
        {
            return stack?.Collectible?.Code != null && MatchesPortionCode(stack.Collectible.Code, CollodionPortionCode);
        }

        private static bool IsCollodionOrContainerWith(ItemStack stack)
        {
            if (IsCollodion(stack)) return true;
            return HasChemicalInAttributes(stack?.Attributes, CollodionPortionCode);
        }

        private static bool TryConsumeCollodion(ItemSlot? activeSlot, int amount)
        {
            if (activeSlot?.Itemstack == null) return false;

            // Directly holding a portion stack.
            if (IsCollodion(activeSlot.Itemstack))
            {
                if (activeSlot.Itemstack.StackSize < amount) return false;
                activeSlot.TakeOut(amount);
                activeSlot.MarkDirty();
                return true;
            }

            // Holding a container (e.g., bucket/jug) with contents stored in attributes.
            if (TryConsumeChemicalFromAttributes(activeSlot.Itemstack.Attributes, CollodionPortionCode, amount))
            {
                activeSlot.MarkDirty();
                return true;
            }

            return false;
        }

        private static bool HasChemicalInAttributes(Vintagestory.API.Datastructures.ITreeAttribute? attrs, AssetLocation portionCode)
        {
            if (attrs == null) return false;

            foreach (var kvp in attrs)
            {
                var attr = kvp.Value;
                if (attr == null) continue;

                if (attr is Vintagestory.API.Datastructures.ItemstackAttribute itemAttr)
                {
                    if (MatchesPortionCode(itemAttr.value?.Collectible?.Code, portionCode)) return true;
                }

                if (attr is Vintagestory.API.Datastructures.TreeArrayAttribute arr && arr.value != null)
                {
                    for (int i = 0; i < arr.value.Length; i++)
                    {
                        var entry = arr.value[i];
                        if (entry == null) continue;
                        if (MatchesPortionCode(entry.GetString("code", null) ?? string.Empty, portionCode)) return true;
                    }
                }

                if (attr is Vintagestory.API.Datastructures.ITreeAttribute subtree)
                {
                    if (HasChemicalInAttributes(subtree, portionCode)) return true;
                }
            }

            return false;
        }

        private static bool TryConsumeChemicalFromAttributes(Vintagestory.API.Datastructures.ITreeAttribute? attrs, AssetLocation portionCode, int amount)
        {
            if (attrs == null) return false;

            foreach (var kvp in attrs)
            {
                var attr = kvp.Value;
                if (attr == null) continue;

                if (attr is Vintagestory.API.Datastructures.ItemstackAttribute itemAttr)
                {
                    ItemStack contained = itemAttr.value;
                    if (MatchesPortionCode(contained?.Collectible?.Code, portionCode))
                    {
                        if (contained == null || contained.StackSize < amount) return false;
                        contained.StackSize -= amount;
                        if (contained.StackSize <= 0)
                        {
                            itemAttr.SetValue(null);
                        }
                        return true;
                    }
                }

                if (attr is Vintagestory.API.Datastructures.TreeArrayAttribute arr && arr.value != null)
                {
                    for (int i = 0; i < arr.value.Length; i++)
                    {
                        var entry = arr.value[i];
                        if (entry == null) continue;

                        string codeStr = entry.GetString("code", null) ?? string.Empty;
                        if (!MatchesPortionCode(codeStr, portionCode)) continue;

                        int stackSize = entry.GetInt("stacksize", entry.GetInt("quantity", -1));
                        if (stackSize < 0)
                        {
                            if (entry.GetBool("makefull", false)) stackSize = 1000;
                        }

                        if (stackSize < amount) return false;

                        stackSize -= amount;
                        entry.SetInt("stacksize", stackSize);
                        entry.RemoveAttribute("makefull");
                        return true;
                    }
                }

                if (attr is Vintagestory.API.Datastructures.ITreeAttribute subtree)
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

            if (candidate.Domain == portionCode.Domain && candidate.Path == portionCode.Path) return true;

            return false;
        }

        private static bool MatchesPortionCode(string candidateCodeStr, AssetLocation portionCode)
        {
            if (string.IsNullOrEmpty(candidateCodeStr)) return false;

            if (candidateCodeStr.Equals(portionCode.ToString(), System.StringComparison.OrdinalIgnoreCase)) return true;
            if (candidateCodeStr.Equals(portionCode.Path, System.StringComparison.OrdinalIgnoreCase)) return true;

            string incontainerPath = $"incontainer-item-{portionCode.Path}";
            if (candidateCodeStr.Equals($"{portionCode.Domain}:{incontainerPath}", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (candidateCodeStr.Equals(incontainerPath, System.StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }
    }
}
