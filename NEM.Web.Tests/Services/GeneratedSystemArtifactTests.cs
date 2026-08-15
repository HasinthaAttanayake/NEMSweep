using System.Text.Json;
using AwesomeAssertions;
using NEM.Contracts;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

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
            "NEM.Web",
            "wwwroot",
            "data",
            "results.json");
        string json = File.ReadAllText(artifactPath);

        SystemDispatchResultsDTO? artifact = JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
            json,
            JsonOptions);

        artifact.Should().NotBeNull();
        artifact!.SchemaVersion.Should().Be(ArtifactSchemaVersions.SystemDispatchResults);
        artifact.Topology.RegionIds.Should().Equal("NSW1", "VIC1");
        artifact.Topology.Links.Select(link => link.Id).Should().Equal("NSW1->VIC1", "VIC1->NSW1");
        artifact.Interconnectors
            .Select(link => link.Id)
            .Should()
            .Equal("NSW1->VIC1", "VIC1->NSW1");
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
                if (File.Exists(Path.Combine(directory.FullName, "NemSim.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the NemSim workspace root.");
    }
}