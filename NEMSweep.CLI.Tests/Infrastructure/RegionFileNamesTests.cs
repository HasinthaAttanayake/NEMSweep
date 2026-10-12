using AwesomeAssertions;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI.Tests.Infrastructure;

public sealed class RegionFileNamesTests
{
    [Theory]
    [InlineData("NSW1", "nsw1")]
    [InlineData("nsw1", "nsw1")]
    public void For_KnownNemRegion_PreservesExistingLowercaseName(string regionId, string expected)
    {
        RegionFileNames.For(regionId).Should().Be(expected);
    }

    [Fact]
    public void For_CustomRegion_EncodesCaseInsensitiveIdentityAsPortableHex()
    {
        RegionFileNames.For("custom region").Should().Be("region-435553544F4D20524547494F4E");
        RegionFileNames.For("CUSTOM REGION").Should().Be("region-435553544F4D20524547494F4E");
    }

    [Fact]
    public void For_DistinctCustomIdentities_DoNotCollide()
    {
        RegionFileNames.For("north").Should().NotBe(RegionFileNames.For("northwest"));
    }

    [Fact]
    public void For_CustomRegion_DoesNotExposeRawPathCharacters()
    {
        string fileName = RegionFileNames.For(@"../west\zone");

        fileName.Should().StartWith("region-");
        fileName.Should().MatchRegex("^region-[0-9A-F]+$");
    }

    [Fact]
    public void For_ExcessivelyLongCustomRegion_ThrowsExplicitException()
    {
        var act = () => RegionFileNames.For(new string('x', 125));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*too long*portable file name*");
    }
}
