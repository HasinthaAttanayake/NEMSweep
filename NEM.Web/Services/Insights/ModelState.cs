using NEM.Contracts;

namespace NEM.Web.Services.Insights;

/// <summary>One capability of the model, and the evidence on this site that it is working.</summary>
public sealed record ModelCapability(string Name, string Detail, bool IsPresent);

/// <summary>
/// What the model can currently do, read out of the artifacts it produced rather than maintained
/// by hand. A statement of scope written as prose goes stale the moment a layer lands; this one
/// changes when the published run changes, which is the only way a progress site stays honest.
/// </summary>
public sealed record ModelState(
    IReadOnlyList<ModelCapability> Capabilities,
    IReadOnlyDictionary<string, int> SchemaVersions)
{
    public static ModelState From(
        SystemDispatchResultsDTO result,
        IReadOnlyList<SweepIndexDTO> sweeps)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sweeps);

        int intervals = result.DataSeries.Demand.TotalDemandMw?.Length ?? 0;
        DispatchInterconnectorDTO[] links = result.Interconnectors ?? [];
        int activeLinks = links.Count(link => link.FlowMw is { Length: > 0 } flow && flow.Any(value => value > 0));
        bool storageSized = result.StorageSizing.Outcome != StorageSizingOutcome.NotRequired;
        bool transmissionCosted = result.Cost.AnnualisedTransmissionCostAud != 0;
        int totalRuns = sweeps.Sum(sweep => sweep.Points.Length);

        var capabilities = new List<ModelCapability>
        {
            new(
                "Regions simulated together",
                result.RegionIds.Length == 1
                    ? $"{RegionNames.State(result.RegionIds[0])} alone"
                    : string.Join(" and ", result.RegionIds.Select(RegionNames.State))
                        + ", dispatched as one system with regional results kept separately",
                result.RegionIds.Length > 0),
            new(
                "Directed inter-regional flow",
                links.Length == 0
                    ? "No interconnectors in the published run"
                    : $"{links.Length} directed {(links.Length == 1 ? "link" : "links")}, "
                        + $"{activeLinks} carrying energy, with losses metered separately",
                links.Length > 0),
            new(
                "Hourly merit-order dispatch",
                intervals == 0
                    ? "No interval series published"
                    : $"{intervals:N0} intervals at {result.Resolution.TotalHours:N0}-hour resolution, "
                        + "with curtailment and unserved demand recorded per interval",
                intervals > 0),
            new(
                "Coupled storage sizing",
                storageSized
                    ? $"Storage grown to {result.StorageSizing.FinalEnergyMwh:N0} MWh over "
                        + $"{result.StorageSizing.PassesUsed} dispatch "
                        + $"{(result.StorageSizing.PassesUsed == 1 ? "pass" : "passes")} to reach the "
                        + "reliability target"
                    : "The installed fleet met the reliability target without resizing",
                true),
            new(
                "Reliability against a standard",
                result.Reliability.StandardName is { Length: > 0 } standard
                    ? $"{standard}: {result.Reliability.TargetUsePercentageOfDemand:G3}% of demand "
                        + "unserved, checked per region and for the system"
                    : "Unserved energy checked against a configured target",
                true),
            new(
                "Annualised system cost",
                transmissionCosted
                    ? "Generation, storage and transmission assets, levelised per MWh served"
                    : "Generation and storage assets, levelised per MWh served; transmission is "
                        + "calculated but priced at zero in this run",
                true),
            new(
                "Scenario sweeps",
                sweeps.Count == 0
                    ? "No sweeps published"
                    : $"{sweeps.Count} published, {totalRuns:N0} runs in total, each openable as a "
                        + "full year of hourly dispatch",
                sweeps.Count > 0),
        };

        return new ModelState(capabilities, CollectSchemaVersions(result, sweeps));
    }

    /// <summary>
    /// The schema version of every artifact behind the page. A reader checking a figure against the
    /// raw JSON needs to know which shape they are opening.
    /// </summary>
    private static IReadOnlyDictionary<string, int> CollectSchemaVersions(
        SystemDispatchResultsDTO result,
        IReadOnlyList<SweepIndexDTO> sweeps)
    {
        var versions = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["System dispatch"] = result.SchemaVersion,
        };

        foreach (SweepIndexDTO sweep in sweeps)
        {
            versions["Sweep index"] = sweep.SchemaVersion;
            foreach ((string name, int version) in sweep.Provenance.SchemaVersions)
            {
                versions[Humanise(name)] = version;
            }
        }

        return versions;
    }

    /// <summary>Turns a camel-cased artifact name from provenance into something readable.</summary>
    private static string Humanise(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        IEnumerable<char> spaced = name.SelectMany((character, index) =>
            index > 0 && char.IsUpper(character) ? [' ', char.ToLowerInvariant(character)] : new[] { character });
        return char.ToUpperInvariant(name[0]) + new string([.. spaced])[1..];
    }
}
