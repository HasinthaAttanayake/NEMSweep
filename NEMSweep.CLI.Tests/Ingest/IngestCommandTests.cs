using AwesomeAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI.Tests.Ingest;

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

    [Theory]
    [InlineData("--validate-inputs")]
    [InlineData("--ingest")]
    public void Commands_UnpublishableLastRegionRejectsCompleteBundleBeforeWriting(string command)
    {
        using var fixture = new BundleFixture(["NSW1", "Coastal"], schemaVersion: 2);
        string regionId = new('\u03b1', 51);
        string manifestPath = Path.Combine(fixture.BundlePath, "manifest.json");
        JsonNode manifest = JsonNode.Parse(File.ReadAllBytes(manifestPath))!;
        manifest["regions"]![1]!["regionId"] = regionId;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        string archivePath = Path.Combine(fixture.BundlePath, "demand", "operational-demand-hh",
            $"{RegionFileNames.For("Coastal")}.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("demand.csv")!;
            string csv;
            using (var reader = new StreamReader(entry.Open()))
            {
                csv = reader.ReadToEnd();
            }
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("demand.csv").Open());
            writer.Write(csv.Replace(",Coastal,", $",{regionId},", StringComparison.Ordinal));
        }

        (int exitCode, _, string error) = fixture.Run(command);

        exitCode.Should().Be(1);
        error.Should().Contain("cannot be published safely").And.Contain("100 UTF-8 bytes");
        Directory.Exists(fixture.OutputRoot).Should().BeFalse();
    }

    [Fact]
    public void ValidateInputs_V2BundleNeedsNoGenerationWorkbook()
    {
        const string regionId = "Coastal / Grid";
        using var fixture = new BundleFixture(
            [regionId],
            schemaVersion: 2,
            weatherDirectories: new Dictionary<string, string> { [regionId] = "coastal" });

        (int exitCode, string output, string error) = fixture.Run("--validate-inputs");

        exitCode.Should().Be(0);
        output.Should().Contain($"Demand {regionId}: valid")
            .And.Contain($"Weather {regionId}: valid")
            .And.Contain("Validated input bundle")
            .And.NotContain("Generation information:");
        error.Should().BeEmpty();
        Directory.Exists(fixture.OutputRoot).Should().BeFalse();
    }

    [Fact]
    public void Ingest_V2BundleWritesSafeNamesWithoutGenerationAndPreservesRegionIdentity()
    {
        const string regionId = "Coastal / Grid";
        using var fixture = new BundleFixture(
            [regionId],
            schemaVersion: 2,
            weatherDirectories: new Dictionary<string, string> { [regionId] = "coastal" });
        string stem = RegionFileNames.For(regionId);

        (int exitCode, string output, string error) = fixture.Run("--ingest");

        exitCode.Should().Be(0);
        error.Should().BeEmpty();
        Directory.GetFiles(fixture.OutputRoot).Select(Path.GetFileName).Should().BeEquivalentTo(
            $"demand-{stem}.json",
            $"weather-{stem}.json");
        output.Should().NotContain("Wrote generation information");
        using JsonDocument weather = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(fixture.OutputRoot, $"weather-{stem}.json")));
        weather.RootElement.GetProperty("regionId").GetString().Should().Be(regionId);
        File.ReadAllText(Path.Combine(fixture.OutputRoot, $"demand-{stem}.json"))
            .Should().Contain(regionId);
    }
}
