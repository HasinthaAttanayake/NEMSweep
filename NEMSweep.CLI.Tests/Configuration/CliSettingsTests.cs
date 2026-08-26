using System.Text.Json;
using AwesomeAssertions;
using NEMSweep.CLI.Configuration;

namespace NEMSweep.CLI.Tests.Configuration;

public sealed class CliSettingsTests
{
    [Fact]
    public void ExampleFile_ContainsOnlyTheFourCliSettings()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "appsettings.example.json")));

        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
                ["inputBundleRoot", "dataRoot", "outputRoot", "defaultScenarioPath"],
                options => options.WithStrictOrdering());
    }

    [Fact]
    public void Load_UsesExampleWhenLocalFileIsAbsent()
    {
        using var fixture = new SettingsFixture();
        fixture.Write(
            "appsettings.example.json",
            "example-inputs",
            "example-data",
            "example-output",
            "example-scenario.json");

        CliSettings settings = CliSettings.Load(fixture.RootPath);

        settings.Should().Be(new CliSettings(
            "example-inputs",
            "example-data",
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

        public void Write(
            string fileName,
            string inputBundleRoot,
            string dataRoot,
            string outputRoot,
            string defaultScenarioPath)
        {
            File.WriteAllText(
                Path.Combine(RootPath, fileName),
                JsonSerializer.Serialize(
                    new { inputBundleRoot, dataRoot, outputRoot, defaultScenarioPath }));
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}