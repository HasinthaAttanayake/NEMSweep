using AwesomeAssertions;
using NEM.CLI.Tests.Ingest;

namespace NEM.CLI.Tests.Demand;

/// <summary>
/// Covers where <c>--import-demand</c> writes, which is the part of it easiest to break. With no
/// argument it writes per-region files under the configured <c>outputRoot</c>; with an argument it
/// writes them under that directory instead and leaves <c>outputRoot</c> alone.
/// </summary>
public sealed class ImportDemandCommandTests
{
    [Fact]
    public void ImportDemand_NoArgumentWritesPerRegionFilesUnderConfiguredOutputRoot()
    {
        using var fixture = new BundleFixture(["NSW1", "QLD1"]);

        (int exitCode, string output, string error) = fixture.Run("--import-demand");

        exitCode.Should().Be(0);
        error.Should().BeEmpty();
        Directory.GetFiles(fixture.OutputRoot).Select(Path.GetFileName).Should().BeEquivalentTo(
            "demand-nsw1.json", "demand-qld1.json");
        output.Should().Contain("Wrote demand data to:");
    }

    [Fact]
    public void ImportDemand_ArgumentWritesPerRegionFilesUnderTheRequestedDirectory()
    {
        using var fixture = new BundleFixture(["NSW1"]);
        string requestedDirectory = Path.Combine(fixture.RootPath, "elsewhere");

        (int exitCode, _, string error) = fixture.Run("--import-demand", requestedDirectory);

        exitCode.Should().Be(0);
        error.Should().BeEmpty();
        Directory.GetFiles(requestedDirectory).Select(Path.GetFileName).Should().BeEquivalentTo(
            "demand-nsw1.json");
        Directory.Exists(fixture.OutputRoot).Should().BeFalse();
    }
}
