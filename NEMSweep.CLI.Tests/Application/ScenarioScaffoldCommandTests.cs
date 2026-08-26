using AwesomeAssertions;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Tests.Application;

/// <summary>
/// The scaffold is the first file a newcomer edits, so the thing worth guarding is that it still
/// loads. A scaffold that has drifted out of the schema teaches the wrong shape.
/// </summary>
public sealed class ScenarioScaffoldCommandTests
{
    [Fact]
    public void NewScenario_EmitsAConfigTheValidatorAccepts()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nemsweep-scaffold-{Guid.NewGuid():N}.json");
        try
        {
            using var output = new StringWriter();
            ScenarioScaffoldCommand.Run(output).Should().Be(0);
            File.WriteAllText(path, output.ToString());

            ScenarioSettings scenario = ScenarioConfig.Load(path);

            scenario.SchemaVersion.Should().Be(ArtifactSchemaVersions.ScenarioConfig);
            scenario.Regions.Should().ContainSingle();
            scenario.Regions[0].GeneratingFleets.Should().ContainSingle();
            scenario.Regions[0].StorageFleets.Should().ContainSingle();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NewScenario_NamesTheArtifactsTheDataRootIsSearchedFor()
    {
        using var output = new StringWriter();
        ScenarioScaffoldCommand.Run(output);

        // The file names, not paths: the data root is the single place they are looked for, and a
        // scaffold carrying a path would teach otherwise.
        output.ToString().Should().Contain("\"demandFile\": \"demand-nsw1.json\"");
        output.ToString().Should().Contain("\"weatherFile\": \"weather-nsw1.json\"");
        output.ToString().Should().NotContain("wwwroot");
    }

    [Fact]
    public void NewScenario_AnswersWithoutReadingSettings()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        // A directory with no settings file in it: the scaffold must not need one.
        int exitCode = new CommandRouter(
            Path.Combine(Path.GetTempPath(), $"nemsweep-absent-{Guid.NewGuid():N}"),
            Path.GetTempPath(),
            output,
            error).Run(["--new-scenario"]);

        exitCode.Should().Be(0);
        error.ToString().Should().BeEmpty();
    }
}
