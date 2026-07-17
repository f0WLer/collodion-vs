using Xunit;
using Photocore.Plates;
using Photocore.Tray;

namespace Photocore.Tests.Tray;

// IsPreDevelopmentExposure and IsOnDemandReclaimable are the reclaim feature's two pivots: the first
// decides which plates still carry the ownership gate, the second decides which plates reclaim on
// demand at all (versus only once dried, the pre-existing behavior). These lock in both mappings.
public class ReclaimEligibilityTests
{
    [Theory]
    [InlineData(PlateStage.Exposed)]
    [InlineData(PlateStage.ExposurePaused)]
    public void UnsealedExposuresArePreDevelopment(PlateStage stage)
    {
        Assert.True(BlockDevelopmentTray.IsPreDevelopmentExposure(stage));
    }

    // Developing onward must fall outside this band, or the ownership gate would keep applying after the
    // exposure is sealed -- past which the plate no longer records who exposed it and nothing could pass.
    [Theory]
    [InlineData(PlateStage.Developing)]
    [InlineData(PlateStage.Developed)]
    [InlineData(PlateStage.Finished)]
    [InlineData(PlateStage.Sensitized)]
    public void SealedOrUnexposedPlatesAreNotPreDevelopment(PlateStage stage)
    {
        Assert.False(BlockDevelopmentTray.IsPreDevelopmentExposure(stage));
    }

    // Everything from an unsealed exposure through a finished photograph reclaims on demand, at the
    // flat doubled cost -- no dryness required, since throwing away a good photo is the player's call.
    [Theory]
    [InlineData(PlateStage.Exposed)]
    [InlineData(PlateStage.ExposurePaused)]
    [InlineData(PlateStage.Developing)]
    [InlineData(PlateStage.Developed)]
    [InlineData(PlateStage.Finished)]
    public void ExposedThroughFinishedReclaimOnDemand(PlateStage stage)
    {
        Assert.True(BlockDevelopmentTray.IsOnDemandReclaimable(stage));
    }

    // Sensitized (and earlier) plates keep the original dried-only salvage rule -- these must stay
    // false or the baseline reclaim silently stops requiring dryness.
    [Theory]
    [InlineData(PlateStage.Sensitized)]
    [InlineData(PlateStage.Exposing)]
    [InlineData(PlateStage.Rough)]
    [InlineData(PlateStage.Clean)]
    [InlineData(PlateStage.Sensitizing)]
    [InlineData(PlateStage.Unknown)]
    public void EarlierStagesStayDriedOnly(PlateStage stage)
    {
        Assert.False(BlockDevelopmentTray.IsOnDemandReclaimable(stage));
    }
}
