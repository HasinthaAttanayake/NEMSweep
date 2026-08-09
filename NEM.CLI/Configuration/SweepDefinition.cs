using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Configuration;

internal sealed record SweepDefinition(
    int SchemaVersion,
    string SweepId,
    string Name,
    SweepAxis Axis,
    string BaselineConfigPath,
    SweepPoint[] Points)
{
    private static readonly Regex SafeId = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant);

    public static SweepDefinition Load(string path, RepositoryPaths paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = paths.ResolveConfiguredPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Sweep definition was not found: {path}.", fullPath);
        }

        SweepDefinition definition = JsonSerializer.Deserialize<SweepDefinition>(
            File.ReadAllBytes(fullPath),
            JsonFile.ReadOptions)
            ?? throw new FormatException($"Sweep definition '{path}' is empty.");
        definition.Validate(paths);
        return definition;
    }

    public string BaselineConfigFullPath(RepositoryPaths paths) =>
        paths.ResolveConfiguredPath(BaselineConfigPath);

    private void Validate(RepositoryPaths paths)
    {
        if (SchemaVersion != 1)
        {
            throw new FormatException($"Sweep '{SweepId}': schema version {SchemaVersion} is not supported; expected 1.");
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
        }
    }
}

internal sealed record SweepAxis(string Label, string Unit);

internal sealed record SweepPoint(string PointId, double AxisValue, string Label, JsonObject Overrides);