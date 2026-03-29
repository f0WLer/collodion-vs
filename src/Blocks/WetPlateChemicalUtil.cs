using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Collodion
{
    /// <summary>
    /// Shared helpers for checking and consuming wet-plate chemical portions
    /// (developer, fixer, water, collodion) from an active item slot or from
    /// item attributes on a container.
    ///
    /// Used by <see cref="BlockDevelopmentTray"/> and <see cref="BlockGlassPlate"/>.
    /// </summary>
    internal static class WetPlateChemicalUtil
    {
        /// <summary>Number of developer pours required to fully develop a plate.</summary>
        internal const int DevelopPoursRequired = 5;

        internal static bool IsChemical(ItemStack? stack, AssetLocation code)
        {
            return stack?.Collectible?.Code != null && stack.Collectible.Code == code;
        }

        internal static bool IsChemicalOrContainerWith(ItemStack? stack, AssetLocation code)
        {
            if (IsChemical(stack, code)) return true;
            return HasChemicalInAttributes(stack?.Attributes, code);
        }

        internal static bool HasConsumableChemical(ItemSlot? activeSlot, AssetLocation portionCode, int amount)
        {
            if (activeSlot?.Itemstack == null) return false;

            if (IsChemical(activeSlot.Itemstack, portionCode))
            {
                return activeSlot.Itemstack.StackSize >= amount;
            }

            return HasSufficientChemicalInAttributes(activeSlot.Itemstack.Attributes, portionCode, amount);
        }

        internal static bool HasChemicalInAttributes(ITreeAttribute? attrs, AssetLocation portionCode)
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

        internal static bool TryConsumeChemical(ItemSlot? activeSlot, AssetLocation portionCode, int amount)
        {
            if (activeSlot?.Itemstack == null) return false;

            // Directly holding a portion stack.
            if (IsChemical(activeSlot.Itemstack, portionCode))
            {
                if (activeSlot.Itemstack.StackSize < amount) return false;
                activeSlot.TakeOut(amount);
                activeSlot.MarkDirty();
                return true;
            }

            // Holding a container (e.g., bucket/jug) with contents stored in attributes.
            // We support multiple internal layouts by scanning the attribute tree.
            if (TryConsumeChemicalFromAttributes(activeSlot.Itemstack.Attributes, portionCode, amount))
            {
                activeSlot.MarkDirty();
                return true;
            }

            return false;
        }

        internal static bool TryConsumeChemicalFromAttributes(ITreeAttribute? attrs, AssetLocation portionCode, int amount)
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

        internal static bool HasSufficientChemicalInAttributes(ITreeAttribute? attrs, AssetLocation portionCode, int amount)
        {
            if (attrs == null) return false;

            foreach (var kvp in attrs)
            {
                IAttribute attr = kvp.Value;
                if (attr == null) continue;

                if (attr is ItemstackAttribute itemAttr)
                {
                    ItemStack contained = itemAttr.value;
                    if (MatchesPortionCode(contained?.Collectible?.Code, portionCode))
                    {
                        if (contained != null && contained.StackSize >= amount) return true;
                    }
                }

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
                            if (entry.GetBool("makefull", false)) stackSize = 1000;
                        }

                        if (stackSize >= amount) return true;
                    }
                }

                if (attr is ITreeAttribute subtree)
                {
                    if (HasSufficientChemicalInAttributes(subtree, portionCode, amount)) return true;
                }
            }

            return false;
        }

        internal static bool MatchesPortionCode(AssetLocation? candidate, AssetLocation portionCode)
        {
            if (candidate == null) return false;

            if (candidate == portionCode) return true;

            // Accept in-container variants.
            if (candidate.Domain == portionCode.Domain && candidate.Path == $"incontainer-item-{portionCode.Path}") return true;

            // Some cases store only a path; accept same path when domain matches.
            if (candidate.Domain == portionCode.Domain && candidate.Path == portionCode.Path) return true;

            return false;
        }

        internal static bool MatchesPortionCode(string candidateCodeStr, AssetLocation portionCode)
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
    }
}
