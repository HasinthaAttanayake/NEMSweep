using AwesomeAssertions;
using NEMSweep.Web.Services;

namespace NEMSweep.Web.Tests.Services;

public sealed class RegionNamesTests
{
    [Fact]
    public void StateList_ReadsAsASentenceRatherThanAChainOfAnds()
    {
        RegionNames.StateList(["NSW1", "QLD1", "SA1", "TAS1", "VIC1"]).Should().Be(
            "New South Wales, Queensland, South Australia, Tasmania and Victoria");
    }

    [Fact]
    public void FullList_KeepsTheIdentifierBesideEachState()
    {
        RegionNames.FullList(["VIC1", "TAS1"]).Should().Be("Victoria (VIC1) and Tasmania (TAS1)");
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "NSW1" }, "New South Wales")]
    [InlineData(new[] { "NSW1", "VIC1" }, "New South Wales and Victoria")]
    public void StateList_NeedsNoCommaBelowThreeNames(string[] regionIds, string expected)
    {
        RegionNames.StateList(regionIds).Should().Be(expected);
    }

    /// <summary>An identifier the site does not know is written through unchanged.</summary>
    [Fact]
    public void StateList_PassesAnUnknownIdentifierThrough()
    {
        RegionNames.StateList(["NSW1", "XYZ9"]).Should().Be("New South Wales and XYZ9");
    }
}
