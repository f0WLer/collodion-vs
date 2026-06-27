using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Photocore.Plates
{
    // Tracks fine sensitization progress on a placed glass plate: which chemistry it's becoming and how
    // many recipe steps are done. The block code still carries the coarse visual (clean/coated); this
    // carries the rest, so we don't need a block variant per (chemistry, step).
    public class BlockEntityGlassPlate : BlockEntity
    {
        public string? ChemistryId;
        public int StepIndex; // number of completed recipe steps; current step = recipe.Steps[StepIndex]

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            // Migration: a coated block with no stored progress comes from a pre-rework save, where
            // "coated" meant "collodion poured, needs silver" — i.e. iodide with one step done.
            if (ChemistryId == null && Block?.Code?.Path?.EndsWith("-coated") == true)
            {
                ChemistryId = PlateAttributes.ChemistryCollodion; // "iodide"
                StepIndex = 1;
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            ChemistryId = tree.GetString("chemistryId", null);
            StepIndex = tree.GetInt("stepIndex", 0);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (ChemistryId != null) tree.SetString("chemistryId", ChemistryId);
            tree.SetInt("stepIndex", StepIndex);
        }
    }
}
