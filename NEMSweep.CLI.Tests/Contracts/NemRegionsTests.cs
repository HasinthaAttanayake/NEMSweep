using AwesomeAssertions;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Tests.Contracts;

public sealed class NemRegionsTests
{
    [Fact]
    public void IsKnown_KnownRegion_IsCaseInsensitive()
    {
        NemRegions.IsKnown("nsw1").Should().BeTrue();
    }

    [Theory]
    [InlineData("NARNIA9")]
    [InlineData(null)]
    public void IsKnown_UnknownRegion_ReturnsFalse(string? regionId)
    {
        NemRegions.IsKnown(regionId).Should().BeFalse();
    }
}