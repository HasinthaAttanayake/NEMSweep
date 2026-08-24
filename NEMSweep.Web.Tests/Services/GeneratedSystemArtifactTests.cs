using System.Text.Json;
using AwesomeAssertions;
using NEMSweep.Contracts;
using NEMSweep.Web.Services;

namespace NEMSweep.Web.Tests.Services;

public sealed class GeneratedSystemArtifactTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void RootSystemArtifact_ContainsValidDirectedInterconnectorEvidence()
    {
        string artifactPath = Path.Combine(
            FindWorkspaceRoot(),
            "NEMSweep.Web",
            "wwwroot",
            "data",
            "results.json");
        string json = File.ReadAllText(artifactPath);

        SystemDispatchResultsDTO? artifact = JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
            json,
            JsonOptions);

        artifact.Should().NotBeNull();
        artifact!.SchemaVersion.Should().Be(ArtifactSchemaVersions.SystemDispatchResults);
        artifact.Topology.RegionIds.Should().Equal("NSW1", "QLD1", "SA1", "TAS1", "VIC1");
        string[] expectedLinks =
        [
            "NSW1->QLD1", "QLD1->NSW1", "VIC1->NSW1", "NSW1->VIC1", "TAS1->VIC1",
            "VIC1->TAS1", "VIC1->SA1", "SA1->VIC1", "NSW1->SA1", "SA1->NSW1",
        ];
        artifact.Topology.Links.Select(link => link.Id).Should().Equal(expectedLinks);
        artifact.Interconnectors
            .Select(link => link.Id)
            .Should()
            .Equal(expectedLinks);
        DispatchArtifactValidator.Validate(artifact).Should().BeNull();
    }

    private static string FindWorkspaceRoot()
    {
        foreach (string startingPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (DirectoryInfo? directory = new DirectoryInfo(startingPath);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "NEMSweep.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the NEMSweep workspace root.");
    }
}