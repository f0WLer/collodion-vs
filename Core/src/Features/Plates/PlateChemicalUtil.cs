using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Photocore.Plates
{
    internal static class PlateChemicalUtil
    {
        internal static bool IsChemicalOrContainerWith(ItemStack? stack, AssetLocation code)
        {
            return HasChemicalInAttributes(stack?.Attributes, code);
        }

        internal static bool HasConsumableChemical(ItemSlot? activeSlot, AssetLocation portionCode, int amount)
        {
            if (activeSlot?.Itemstack == null) return false;
            return HasSufficientChemicalInAttributes(activeSlot.Itemstack.Attributes, portionCode, amount);
        }

        internal static bool HasChemicalInAttributes(ITreeAttribute? attrs, AssetLocation portionCode)
        {
            return TraverseChemicalEntries(
                attrs,
                (itemAttr, contained) => MatchesPortionCode(contained?.Collectible?.Code, portionCode)
                    ? TraverseDecision.StopSuccess
                    : TraverseDecision.Continue,
                (entry, codeStr, stackSize) => MatchesPortionCode(codeStr, portionCode)
                    ? TraverseDecision.StopSuccess
                    : TraverseDecision.Continue
            ) == TraverseDecision.StopSuccess;
        }

        internal static bool TryConsumeChemical(ItemSlot? activeSlot, AssetLocation portionCode, int amount)
        {
            if (activeSlot?.Itemstack == null) return false;

            if (TryConsumeChemicalFromAttributes(activeSlot.Itemstack.Attributes, portionCode, amount))
            {
                activeSlot.MarkDirty();
                return true;
            }

            return false;
        }

        internal static bool TryConsumeChemicalFromAttributes(ITreeAttribute? attrs, AssetLocation portionCode, int amount)
        {
            return TraverseChemicalEntries(
                attrs,
                (itemAttr, contained) =>
                {
                    if (!MatchesPortionCode(contained?.Collectible?.Code, portionCode)) return TraverseDecision.Continue;
                    if (contained == null || contained.StackSize < amount) return TraverseDecision.StopFailure;

                    contained.StackSize -= amount;
                    if (contained.StackSize <= 0)
                    {
                        itemAttr.SetValue(null);
                    }

                    return TraverseDecision.StopSuccess;
                },
                (entry, codeStr, stackSize) =>
                {
                    if (!MatchesPortionCode(codeStr, portionCode)) return TraverseDecision.Continue;
                    if (stackSize < amount) return TraverseDecision.StopFailure;

                    int remaining = stackSize - amount;
                    entry.SetInt("stacksize", remaining);
                    entry.RemoveAttribute("makefull");
                    return TraverseDecision.StopSuccess;
                }
            ) == TraverseDecision.StopSuccess;
        }

        internal static bool HasSufficientChemicalInAttributes(ITreeAttribute? attrs, AssetLocation portionCode, int amount)
        {
            return TraverseChemicalEntries(
                attrs,
                (itemAttr, contained) =>
                {
                    if (!MatchesPortionCode(contained?.Collectible?.Code, portionCode)) return TraverseDecision.Continue;
                    return contained != null && contained.StackSize >= amount
                        ? TraverseDecision.StopSuccess
                        : TraverseDecision.StopFailure;
                },
                (entry, codeStr, stackSize) =>
                {
                    if (!MatchesPortionCode(codeStr, portionCode)) return TraverseDecision.Continue;
                    return stackSize >= amount ? TraverseDecision.StopSuccess : TraverseDecision.StopFailure;
                }
            ) == TraverseDecision.StopSuccess;
        }

        private enum TraverseDecision
        {
            Continue,
            StopSuccess,
            StopFailure
        }

        // VS stores liquid contents in container item attributes in two shapes depending on the container type:
        // ItemstackAttribute (e.g. bowls) or TreeArrayAttribute entries with a "code" string key (e.g. buckets).
        // The incontainer-item-{code} path prefix is VS's internal rename when an item is placed inside a container.
        private static TraverseDecision TraverseChemicalEntries(
            ITreeAttribute? attrs,
            System.Func<ItemstackAttribute, ItemStack?, TraverseDecision> onItemstack,
            System.Func<ITreeAttribute, string, int, TraverseDecision> onTreeEntry)
        {
            if (attrs == null) return TraverseDecision.Continue;

            foreach (var kvp in attrs)
            {
                IAttribute attr = kvp.Value;
                if (attr == null) continue;

                if (attr is ItemstackAttribute itemAttr)
                {
                    TraverseDecision decision = onItemstack(itemAttr, itemAttr.value);
                    if (decision != TraverseDecision.Continue) return decision;
                }

                if (attr is TreeArrayAttribute arr && arr.value != null)
                {
                    for (int i = 0; i < arr.value.Length; i++)
                    {
                        ITreeAttribute entry = arr.value[i];
                        if (entry == null) continue;

                        string codeStr = entry.GetString("code", null) ?? string.Empty;
                        int stackSize = ReadEntryStackSize(entry);

                        TraverseDecision decision = onTreeEntry(entry, codeStr, stackSize);
                        if (decision != TraverseDecision.Continue) return decision;
                    }
                }

                if (attr is ITreeAttribute subtree)
                {
                    TraverseDecision decision = TraverseChemicalEntries(subtree, onItemstack, onTreeEntry);
                    if (decision != TraverseDecision.Continue) return decision;
                }
            }

            return TraverseDecision.Continue;
        }

        private static int ReadEntryStackSize(ITreeAttribute entry)
        {
            int stackSize = entry.GetInt("stacksize", entry.GetInt("quantity", -1));
            if (stackSize >= 0) return stackSize;

            return entry.GetBool("makefull", false) ? 1000 : -1;
        }

        internal static bool MatchesPortionCode(AssetLocation? candidate, AssetLocation portionCode)
        {
            if (candidate == null) return false;

            if (candidate == portionCode) return true;

            if (candidate.Domain == portionCode.Domain && candidate.Path == $"incontainer-item-{portionCode.Path}") return true;
            if (candidate.Domain == portionCode.Domain && candidate.Path == portionCode.Path) return true;

            return false;
        }

        internal static bool MatchesPortionCode(string candidateCodeStr, AssetLocation portionCode)
        {
            if (string.IsNullOrEmpty(candidateCodeStr)) return false;

            if (MatchesPortionCode(new AssetLocation(candidateCodeStr), portionCode)) return true;

            if (candidateCodeStr.Equals(portionCode.Path, StringComparison.OrdinalIgnoreCase)) return true;
            string incontainerPath = $"incontainer-item-{portionCode.Path}";
            return candidateCodeStr.Equals(incontainerPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
