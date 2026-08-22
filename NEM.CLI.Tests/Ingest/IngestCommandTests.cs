using AwesomeAssertions;

namespace NEM.CLI.Tests.Ingest;

public sealed class IngestCommandTests
{
    [Fact]
    public void ValidateInputs_ValidBundleReturnsZeroWithoutWritingOutputs()
    {
        using var fixture = new BundleFixture(["NSW1"]);

        (int exitCode, string output, string error) = fixture.Run("--validate-inputs");

        exitCode.Should().Be(0);
        output.Should().Contain("Demand NSW1: valid");
        output.Should().Contain("Weather NSW1: valid");
        error.Should().BeEmpty();
        Directory.Exists(fixture.OutputRoot).Should().BeFalse();
    }

    [Fact]
    public void ValidateInputs_MissingDemandWeekFailsWithRegionAndWritesNothing()
    {
        using var fixture = new BundleFixture(["NSW1", "QLD1"], 7);
        fixture.ReplaceDemandRows("QLD1", 6);

        (int exitCode, _, string error) = fixture.Run("--validate-inputs");

        exitCode.Should().Be(1);
        error.Should().Contain("QLD1").And.Contain("missing interval");
        Directory.Exists(fixture.OutputRoot).Should().BeFalse();
    }

    [Fact]
    public void Ingest_TwoRegionsWritesExpectedArtifacts()
    {
        using var fixture = new BundleFixture(["NSW1", "QLD1"]);
        (int exitCode, string output, string error) = fixture.Run("--ingest");

        exitCode.Should().Be(0);
        error.Should().BeEmpty();
        Directory.GetFiles(fixture.OutputRoot).Select(Path.GetFileName).Should().BeEquivalentTo(
            "demand-nsw1.json", "demand-qld1.json", "weather-nsw1.json", "weather-qld1.json",
            "generation-information.json");
        output.Should().Contain("Wrote generation information");
    }

    [Fact]
    public void Ingest_InvalidBundleWritesNoArtifacts()
    {
        using var fixture = new BundleFixture(["NSW1"], 7);
        fixture.ReplaceDemandRows("NSW1", 6);

        (int exitCode, _, string error) = fixture.Run("--ingest");

        exitCode.Should().Be(1);
        error.Should().Contain("NSW1").And.Contain("missing interval");
        Directory.Exists(fixture.OutputRoot).Should().BeFalse();
    }
}
