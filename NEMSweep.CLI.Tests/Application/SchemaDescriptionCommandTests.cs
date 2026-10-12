using System.Text.Json.Nodes;
using AwesomeAssertions;
using NEMSweep.CLI.Application;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Tests.Application;

public sealed class SchemaDescriptionCommandTests
{
    [Theory]
    [InlineData("manifest", "manifest-v2.json", 2)]
    [InlineData("legacy-manifest", "legacy-manifest-v1.json", 1)]
    [InlineData("demand", "demand-v2.json", 2)]
    [InlineData("weather", "weather-v6.json", 6)]
    [InlineData("scenario", "scenario-v7.json", 7)]
    [InlineData("sweep", "sweep-v2.json", 2)]
    public void DescribeSchema_EmitsVersionedDraft202012Document(string format, string filename, int version)
    {
        using var output = new StringWriter();

        SchemaDescriptionCommand.Run(output, format).Should().Be(0);

        JsonObject schema = JsonNode.Parse(output.ToString())!.AsObject();
        schema["$schema"]!.GetValue<string>().Should().Be("https://json-schema.org/draft/2020-12/schema");
        schema["$id"]!.GetValue<string>().Should().EndWith($"/{filename}");
        schema["properties"]!["schemaVersion"]!["const"]!.GetValue<int>().Should().Be(version);
    }

    [Fact]
    public void ScenarioSchema_DescribesFixedFleetAndBoundedBatterySizing()
    {
        using var output = new StringWriter();
        SchemaDescriptionCommand.Run(output, "scenario");
        JsonObject schema = JsonNode.Parse(output.ToString())!.AsObject();

        schema["required"]!.AsArray().Select(value => value!.GetValue<string>())
            .Should().Contain("reliability").And.NotContain("storageSizing");
        schema["properties"]!["executionMode"]!["default"]!.GetValue<string>().Should().Be("fixedFleet");
        schema["properties"]!["executionMode"]!["enum"]!.AsArray()
            .Select(value => value!.GetValue<string>()).Should().Equal("fixedFleet", "sizeBattery");
        schema["allOf"]![0]!["then"]!["required"]!.AsArray()
            .Select(value => value!.GetValue<string>()).Should().Contain("storageSizing");

        JsonObject sizing = schema["$defs"]!["storageSizing"]!.AsObject();
        sizing["required"]!.AsArray().Select(value => value!.GetValue<string>())
            .Should().Equal("maximumPowerMw", "maximumEnergyMwh");
        sizing["properties"]!.AsObject().Select(property => property.Key)
            .Should().BeEquivalentTo("maximumPowerMw", "maximumEnergyMwh", "maximumPasses");
        schema["$defs"]!["reliability"]!["properties"]!["targetUsePercentage"]!["exclusiveMinimum"]!
            .GetValue<double>().Should().Be(0);
        schema["$defs"]!["region"]!["properties"]!["storageFleets"]!["minItems"].Should().BeNull();
        schema["$defs"]!["region"]!["properties"]!["regionId"]!["pattern"]!.GetValue<string>().Should().Contain("\\S");
        schema["$defs"]!["region"]!["properties"]!["regionId"]!["description"]!.GetValue<string>()
            .Should().Contain("100 UTF-8 bytes");
        JsonObject batteryRequirement = schema["allOf"]![0]!["then"]!["properties"]!["regions"]!["items"]!["properties"]!["storageFleets"]!.AsObject();
        batteryRequirement["minContains"]!.GetValue<int>().Should().Be(1);
        batteryRequirement["contains"]!["properties"]!["technology"]!["const"]!.GetValue<string>().Should().Be("Battery");
    }

    [Fact]
    public void ManifestSchemas_SeparateStrictV2MappingFromPermissiveLegacyV1()
    {
        using var currentOutput = new StringWriter();
        using var legacyOutput = new StringWriter();
        SchemaDescriptionCommand.Run(currentOutput, "manifest");
        SchemaDescriptionCommand.Run(legacyOutput, "legacy-manifest");
        JsonObject current = JsonNode.Parse(currentOutput.ToString())!.AsObject();
        JsonObject legacy = JsonNode.Parse(legacyOutput.ToString())!.AsObject();

        current["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        current["properties"]!["$schema"].Should().NotBeNull();
        current["$defs"]!["region"]!["required"]!.AsArray()
            .Select(value => value!.GetValue<string>()).Should().Equal("regionId", "weatherDirectory");
        current["$defs"]!["region"]!["properties"]!["regionId"]!["pattern"]!.GetValue<string>()
            .Should().Contain("\\S");
        legacy["additionalProperties"].Should().BeNull();
        legacy["properties"]!["regions"]!["items"]!["type"]!.GetValue<string>().Should().Be("string");
    }

    [Fact]
    public void SweepSchema_AllowsModeAndReliabilityOverridesOnlyInCurrentShape()
    {
        using var output = new StringWriter();
        SchemaDescriptionCommand.Run(output, "sweep");
        JsonObject schema = JsonNode.Parse(output.ToString())!.AsObject();
        JsonObject overrides = schema["$defs"]!["scenarioOverrides"]!.AsObject();

        overrides["properties"]!["executionMode"].Should().NotBeNull();
        overrides["properties"]!["reliability"].Should().NotBeNull();
        overrides["properties"]!["dispatch"].Should().BeNull();
        schema["$defs"]!["storageSizingOverride"]!["properties"]!["reliabilityStandardName"]
            .Should().BeNull();
    }

    [Fact]
    public void DescribeSchema_RejectsUnknownFormat()
    {
        using var output = new StringWriter();
        Action action = () => SchemaDescriptionCommand.Run(output, "unknown");

        action.Should().Throw<ArgumentException>().WithMessage("*Schema format*");
    }
}