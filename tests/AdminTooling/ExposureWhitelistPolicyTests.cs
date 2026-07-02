using Xunit;
using Photocore.AdminTooling;

namespace Photocore.Tests.AdminTooling;

public class ExposureWhitelistPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DisabledWhitelistAllowsEveryone(bool isOperator, bool isListed)
    {
        Assert.True(ExposureWhitelistService.ComputeAllowed(false, isOperator, isListed));
    }

    [Fact]
    public void EnabledWhitelistAllowsOperatorEvenWhenNotListed()
    {
        Assert.True(ExposureWhitelistService.ComputeAllowed(true, isOperator: true, isListed: false));
    }

    [Fact]
    public void EnabledWhitelistAllowsListedMember()
    {
        Assert.True(ExposureWhitelistService.ComputeAllowed(true, isOperator: false, isListed: true));
    }

    [Fact]
    public void EnabledWhitelistBlocksUnlistedNonOperator()
    {
        Assert.False(ExposureWhitelistService.ComputeAllowed(true, isOperator: false, isListed: false));
    }
}
