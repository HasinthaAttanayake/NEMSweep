using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Configuration;

internal sealed record SweepDefinition(
    int SchemaVersion,
    string SweepId,
    string Name,
    SweepAxis Axis,
    string BaselineConfigPath,
    SweepPoint[] Points)
{
    private static readonly Regex SafeId = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant);

    public static SweepDefinition Load(string path, WorkspacePaths paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = paths.ResolveConfiguredPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Sweep definition was not found: {path}.", fullPath);
        }

        SweepDefinition definition = JsonFile.ReadConfig<SweepDefinition>(File.ReadAllBytes(fullPath))
            ?? throw new FormatException($"Sweep definition '{path}' is empty.");
        definition.Validate(paths);
        return definition;
    }

    public string BaselineConfigFullPath(WorkspacePaths paths) =>
        paths.ResolveConfiguredPath(BaselineConfigPath);

    private void Validate(WorkspacePaths paths)
    {
        if (SchemaVersion != ArtifactSchemaVersions.SweepDefinition)
        {
            throw new FormatException(
                $"Sweep '{SweepId}': schema version {SchemaVersion} is not supported; "
                + $"expected {ArtifactSchemaVersions.SweepDefinition}.");
        }

        if (string.IsNullOrWhiteSpace(SweepId) || !SafeId.IsMatch(SweepId))
        {
            throw new FormatException($"Sweep '{SweepId}': sweep id must match ^[a-z0-9][a-z0-9-]*$.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new FormatException($"Sweep '{SweepId}': name is required.");
        }

        if (Axis is null || string.IsNullOrWhiteSpace(Axis.Label) || string.IsNullOrWhiteSpace(Axis.Unit))
        {
            throw new FormatException($"Sweep '{SweepId}': axis label and unit are required.");
        }

        if (string.IsNullOrWhiteSpace(BaselineConfigPath) || !File.Exists(BaselineConfigFullPath(paths)))
        {
            throw new FormatException($"Sweep '{SweepId}': baseline config '{BaselineConfigPath}' was not found.");
        }

        if (Points is null || Points.Length == 0)
        {
            throw new FormatException($"Sweep '{SweepId}': at least one point is required.");
        }

        var pointIds = new HashSet<string>(StringComparer.Ordinal);
        var axisValues = new Dictionary<double, string>();
        foreach (SweepPoint? point in Points)
        {
            if (point is null || string.IsNullOrWhiteSpace(point.PointId) || !SafeId.IsMatch(point.PointId))
            {
                throw new FormatException($"Sweep '{SweepId}': point '{point?.PointId}' must have a filename-safe id.");
            }

            if (!pointIds.Add(point.PointId))
            {
                throw new FormatException($"Sweep '{SweepId}': duplicate point id '{point.PointId}'.");
            }

            if (string.IsNullOrWhiteSpace(point.Label))
            {
                throw new FormatException($"Sweep '{SweepId}', point '{point.PointId}': label is required.");
            }

            if (point.Overrides is null)
            {
                throw new FormatException($"Sweep '{SweepId}', point '{point.PointId}': overrides are required.");
            }

            // Nothing in the model reads axisValue: it labels the x-axis and no more, so a wrong one
            // is a silently mislabelled chart rather than a failed run. Two points claiming the same
            // position cannot both be right, and it is the shape a copy-pasted point takes, so it is
            // the one axis mistake that can be caught without knowing what the overrides mean.
            if (!axisValues.TryAdd(point.AxisValue, point.PointId))
            {
                throw new FormatException(
                    $"Sweep '{SweepId}': points '{axisValues[point.AxisValue]}' and '{point.PointId}' "
                    + $"share axis value {point.AxisValue.ToString(CultureInfo.InvariantCulture)}. "
                    + "Each point must sit at its own position on the axis.");
            }
        }
    }
}

internal sealed record SweepAxis(string Label, string Unit);

internal sealed record SweepPoint(string PointId, double AxisValue, string Label, JsonObject Overrides);