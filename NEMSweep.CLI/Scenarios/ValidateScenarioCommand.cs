using System.Text.Json.Nodes;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Scenarios;

/// <summary>
/// Loads a scenario configuration and reports whether it is valid, writing nothing else. There was
/// no validate-only path for a scenario before this: the way to find out whether a config loaded was
/// to run it, which costs a full dispatch to learn that a field name was wrong.
/// </summary>
/// <remarks>
/// With <c>--format json</c> the answer is a single object rather than prose. That matters for the
/// loop the LLM guide already describes, where a generated config is corrected against the error it
/// produced: a caller can read a field it can act on instead of parsing a sentence.
/// </remarks>
internal static class ValidateScenarioCommand
{
    /// <summary>Validates the configured default scenario.</summary>
    /// <param name="context">The invocation's workspace and settings.</param>
    public static int Run(CliContext context) =>
        Run(context, context.LoadSettings().DefaultScenarioPath);

    /// <summary>Validates one scenario configuration.</summary>
    /// <param name="context">The invocation's workspace and settings.</param>
    /// <param name="scenarioConfigPath">Path to the configuration, relative to the working root.</param>
    public static int Run(CliContext context, string scenarioConfigPath)
    {
        ArgumentNullException.ThrowIfNull(context);
        string path = context.Paths.ResolveConfiguredPath(scenarioConfigPath);
        try
        {
            ScenarioSettings scenario = ScenarioConfig.Load(path);
            return Report(context, scenarioConfigPath, scenario, failure: null);
        }
        catch (Exception exception) when (exception
            is FormatException or IOException or ArgumentException or System.Text.Json.JsonException)
        {
            return Report(context, scenarioConfigPath, scenario: null, failure: exception);
        }
    }

    private static int Report(
        CliContext context,
        string path,
        ScenarioSettings? scenario,
        Exception? failure)
    {
        if (context.Format is not OutputFormat.Json)
        {
            WriteText(context, path, scenario, failure);
            return failure is null ? 0 : 1;
        }

        var report = new JsonObject
        {
            ["valid"] = failure is null,
            ["path"] = path,
        };

        if (scenario is not null)
        {
            report["schemaVersion"] = scenario.SchemaVersion;
            report["id"] = scenario.Id;
            report["regions"] = new JsonArray(
                [.. scenario.Regions.Select(region => (JsonNode)region.RegionId!)]);
            report["interconnectors"] = scenario.Interconnectors?.Length ?? 0;
        }

        if (failure is not null)
        {
            report["error"] = new JsonObject
            {
                // The stage a caller would attribute the failure to, matching the vocabulary a sweep
                // point status already uses, so one consumer can read both.
                ["stage"] = SweepFailureStage.Input.ToString(),
                ["code"] = failure is IOException ? "configUnreadable" : "invalidConfig",
                ["message"] = failure.Message,
            };
        }

        context.Output.WriteLine(JsonFile.SerializeExact(report));
        return failure is null ? 0 : 1;
    }

    private static void WriteText(
        CliContext context,
        string path,
        ScenarioSettings? scenario,
        Exception? failure)
    {
        if (failure is not null)
        {
            (context.Error ?? context.Output).WriteLine($"Scenario '{path}' is not valid: {failure.Message}");
            return;
        }

        context.Output.WriteLine(
            $"Scenario '{path}' is valid: {scenario!.Regions.Length} region(s) "
            + $"({string.Join(", ", scenario.Regions.Select(region => region.RegionId))}), "
            + $"{scenario.Interconnectors?.Length ?? 0} interconnector(s).");
    }
}
