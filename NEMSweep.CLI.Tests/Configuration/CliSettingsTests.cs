using System.Text.Json;
using AwesomeAssertions;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI.Tests.Configuration;

public sealed class CliSettingsTests
{
    [Fact]
    public void ExampleFile_ContainsOnlyTheThreeCliSettings()
    {
        RepositoryPaths paths = RepositoryPaths.Discover(AppContext.BaseDirectory);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(paths.SolutionRoot, "NEMSweep.CLI", "appsettings.example.json")));

        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
                ["inputBundleRoot", "outputRoot", "defaultScenarioPath"],
                options => options.WithStrictOrdering());
    }

    [Fact]
    public void Load_UsesExampleWhenLocalFileIsAbsent()
    {
        using var fixture = new SettingsFixture();
        fixture.Write("appsettings.example.json", "example-inputs", "example-output", "example-scenario.json");

        CliSettings settings = CliSettings.Load(fixture.RootPath);

        settings.Should().Be(new CliSettings(
            "example-inputs",
            "example-output",
            "example-scenario.json"));
    }

    private sealed class SettingsFixture : IDisposable
    {
        public SettingsFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"nemsweep-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Write(string fileName, string inputBundleRoot, string outputRoot, string defaultScenarioPath)
        {
            File.WriteAllText(
                Path.Combine(RootPath, fileName),
                JsonSerializer.Serialize(new { inputBundleRoot, outputRoot, defaultScenarioPath }));
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}