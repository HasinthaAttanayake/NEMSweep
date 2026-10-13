using System.Text.Json.Nodes;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.Scenarios;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.QuickStart;

internal static class StarterTemplates
{
    public static void WriteAndValidate(string sampleId, CliContext context)
    {
        JsonObject baseline = ScenarioConfig.UpgradeConfig(Read(
            sampleId == "simple" ? "Starter.Simple" : "Starter.Advanced"));
        SetSchema(baseline, "scenario", ArtifactSchemaVersions.ScenarioConfig);
        string prefix = sampleId == "simple" ? "synthetic-simple" : "australian-nem-fy2026";
        string label = sampleId == "simple" ? "Synthetic simple" : "Australian NEM FY2026 (AEMO demand; original TMYx EPW weather)";
        baseline["id"] = $"{prefix}-baseline";
        baseline["name"] = $"{label} starter baseline (example assumptions)";

        JsonObject sized = (JsonObject)baseline.DeepClone();
        sized["executionMode"] = "sizeBattery";
        sized["id"] = $"{prefix}-sized";
        if (sampleId == "simple")
        {
            sized["storageSizing"] = new JsonObject
            {
                ["maximumPowerMw"] = 30,
                ["maximumEnergyMwh"] = 500,
                ["maximumPasses"] = 64,
            };
        }

        JsonObject fixedScenario = (JsonObject)baseline.DeepClone();
        fixedScenario["executionMode"] = "fixedFleet";
        fixedScenario.Remove("storageSizing");
        fixedScenario["id"] = $"{prefix}-fixed";
        if (sampleId == "simple")
        {
            fixedScenario["regions"]![0]!["generatingFleets"]![0]!["nameplateCapacityMw"] = 80;
            fixedScenario["regions"]![0]!["storageFleets"] = new JsonArray();
        }

        baseline["executionMode"] = "fixedFleet";
        baseline.Remove("storageSizing");
        WriteScenario("fixed-scenario.json", fixedScenario, context);
        WriteScenario("sized-scenario.json", sized, context);
        WriteScenario("mode-sweep-baseline.json", baseline, context);

        var demo = new JsonObject
        {
            ["sweepId"] = $"{prefix}-modes",
            ["name"] = $"{label}: fixed fleet versus battery sizing",
            ["axis"] = new JsonObject { ["label"] = "Execution mode", ["unit"] = "mode index" },
            ["baselineConfigPath"] = "scenarios/mode-sweep-baseline.json",
            ["points"] = new JsonArray(
                new JsonObject
                {
                    ["pointId"] = "fixed", ["axisValue"] = 0, ["label"] = "Declared fleet",
                    ["overrides"] = new JsonObject { ["executionMode"] = "fixedFleet" },
                },
                new JsonObject
                {
                    ["pointId"] = "sized", ["axisValue"] = 1, ["label"] = "Battery sizing",
                    ["overrides"] = new JsonObject
                    {
                        ["executionMode"] = "sizeBattery",
                        ["storageSizing"] = sized["storageSizing"]!.DeepClone(),
                    },
                }),
        };
        WriteSweep("demo.json", demo, context);
        if (sampleId == "advanced")
        {
            JsonObject full = Read("Starter.FullSweep");
            full["sweepId"] = "australian-nem-datacentre-fy2026";
            full["name"] = "Australian NEM FY2026: full data-centre study (25 points; example assumptions)";
            full["baselineConfigPath"] = "scenarios/sized-scenario.json";
            WriteSweep("datacentre-full.json", full, context);
        }
    }

    private static void WriteScenario(string name, JsonObject scenario, CliContext context)
    {
        foreach (JsonNode? region in scenario["regions"]!.AsArray())
        {
            string regionId = region!["regionId"]!.GetValue<string>();
            string stem = RegionFileNames.For(regionId);
            region["demandFile"] = $"demand-{stem}.json";
            region["weatherFile"] = $"weather-{stem}.json";
        }
        string path = context.Paths.ResolveConfiguredPath(Path.Combine("scenarios", name));
        JsonFile.WriteExact(scenario, path);
        _ = ScenarioConfig.Load(path);
    }

    private static void WriteSweep(string name, JsonObject sweep, CliContext context)
    {
        SetSchema(sweep, "sweep", ArtifactSchemaVersions.SweepDefinition);
        string path = context.Paths.ResolveConfiguredPath(Path.Combine("sweeps", name));
        JsonFile.WriteExact(sweep, path);
        (SweepDefinition definition, JsonNode baseline) =
            SweepFanOutCommand.LoadDefinitionAndBaseline(context, path);
        foreach (SweepPoint point in definition.Points)
        {
            // Validate through the same merge and config reader used by an actual sweep.
            SweepFanOutCommand.WritePointConfig(definition, baseline,
                SweepFanOutCommand.ConfigOutputDirectory(context, definition), point, validate: true);
        }
    }

    private static void SetSchema(JsonObject node, string kind, int version)
    {
        node["schemaVersion"] = version;
        node["$schema"] =
            $"https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/{kind}-v{version}.json";
    }

    private static JsonObject Read(string name)
    {
        using Stream stream = typeof(StarterTemplates).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The starter template '{name}' is missing from this CLI build.");
        return JsonNode.Parse(stream)?.AsObject()
            ?? throw new FormatException($"Starter template '{name}' is empty.");
    }
}
