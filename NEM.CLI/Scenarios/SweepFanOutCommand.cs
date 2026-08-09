using System.Text.Json.Nodes;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Scenarios;

internal static class SweepFanOutCommand
{
    public static int Run(CliContext context, string definitionPath)
    {
        WriteConfigs(context, definitionPath, validateGeneratedConfigs: true);
        return 0;
    }

    internal static SweepDefinition WriteConfigs(
        CliContext context,
        string definitionPath,
        bool validateGeneratedConfigs)
    {
        SweepDefinition definition = SweepDefinition.Load(definitionPath, context.Paths);
        JsonNode baseline = JsonNode.Parse(File.ReadAllBytes(definition.BaselineConfigFullPath(context.Paths)))
            ?? throw new FormatException($"Sweep '{definition.SweepId}': baseline config is empty.");
        string outputDirectory = Path.Combine(
            context.Paths.SolutionRoot,
            "sweeps",
            definition.SweepId,
            "configs");

        foreach (SweepPoint point in definition.Points)
        {
            JsonObject config = (JsonObject)JsonMergePatch.Apply(baseline, point.Overrides);
            config["id"] = $"{definition.SweepId}-{point.PointId}";
            config["provenance"] = new JsonObject
            {
                ["sweepId"] = definition.SweepId,
                ["pointId"] = point.PointId,
                ["baselineConfigPath"] = definition.BaselineConfigPath,
            };

            string outputPath = Path.Combine(outputDirectory, $"{point.PointId}.json");
            JsonFile.WriteExact(config, outputPath);
            if (validateGeneratedConfigs)
            {
                _ = CliSettings.LoadScenario(outputPath);
            }
            context.Output.WriteLine($"Wrote scenario config: {Path.GetFullPath(outputPath)}");
        }

        return definition;
    }
}