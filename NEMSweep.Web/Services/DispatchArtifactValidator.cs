using NEMSweep.Contracts;

namespace NEMSweep.Web.Services;

/// <summary>Validates dispatch evidence that is required by the supported artifact schemas.</summary>
public static class DispatchArtifactValidator
{
    private const double FlowToleranceMw = 1e-9;
    private const double ArtifactEnergyToleranceMwh = 0.100001;

    public static string? Validate(object artifact) => artifact switch
    {
        DispatchResultsDTO result => Validate(result),
        SystemDispatchResultsDTO result => Validate(result),
        SystemDispatchOverviewDTO result => Validate(result),
        RegionDispatchResultsDTO result => Validate(result),
        RegionDispatchOverviewDTO result => Validate(result),
        _ => null,
    };

    public static string? Validate(DispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ValidateSeriesAndCost(result.DataSeries, result.Cost);
    }

    public static string? Validate(RegionDispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ValidateSeriesAndCost(result.DataSeries, result.Cost);
    }

    public static string? Validate(RegionDispatchOverviewDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.DeliveredGenerationByTechnologyMwh is null
            || result.DeliveredGenerationByTechnologyMwh.Any(entry =>
                string.IsNullOrWhiteSpace(entry.Key)
                || !double.IsFinite(entry.Value)
                || entry.Value < 0)
            || Math.Abs(
                result.DeliveredGenerationByTechnologyMwh.Values.Sum()
                - result.Metrics.DeliveredGenerationMwh) > ArtifactEnergyToleranceMwh)
        {
            return "Region dispatch overview generation totals are invalid.";
        }

        if (!double.IsFinite(result.TransmissionLossesMwh) || result.TransmissionLossesMwh < 0)
        {
            return "Region dispatch overview transmission losses are invalid.";
        }

        return ValidateCost(result.Cost);
    }

    public static string? Validate(SystemDispatchOverviewDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string? costValidation = ValidateCost(result.Cost);
        return costValidation
            ?? ValidateRegionsAndTopology(result.RegionIds, result.RegionSummariesById, result.Topology);
    }

    public static string? Validate(SystemDispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string? commonValidation = ValidateSeriesAndCost(result.DataSeries, result.Cost)
            ?? ValidateRegionsAndTopology(result.RegionIds, result.RegionSummariesById, result.Topology);
        if (commonValidation is not null)
        {
            return commonValidation;
        }

        if (result.Interconnectors is null)
        {
            return "System dispatch interconnector evidence is missing.";
        }

        int intervalCount = result.DataSeries.Demand.TotalDemandMw.Length;
        var regionIds = new HashSet<string>(result.RegionIds, StringComparer.OrdinalIgnoreCase);
        var topologyById = result.Topology.Links.ToDictionary(link => link.Id, StringComparer.Ordinal);
        var directions = new HashSet<(string From, string To)>();
        for (int linkIndex = 0; linkIndex < result.Interconnectors.Length; linkIndex++)
        {
            DispatchInterconnectorDTO? link = result.Interconnectors[linkIndex];
            if (link is null
                || string.IsNullOrWhiteSpace(link.Id)
                || string.IsNullOrWhiteSpace(link.FromRegionId)
                || string.IsNullOrWhiteSpace(link.ToRegionId)
                || string.Equals(link.FromRegionId, link.ToRegionId, StringComparison.OrdinalIgnoreCase)
                || !regionIds.Contains(link.FromRegionId)
                || !regionIds.Contains(link.ToRegionId))
            {
                return "System dispatch interconnector endpoints do not match its regions.";
            }

            if (!topologyById.TryGetValue(link.Id, out DispatchTopologyLinkDTO? topologyLink)
                || !string.Equals(link.FromRegionId, topologyLink.FromRegionId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(link.ToRegionId, topologyLink.ToRegionId, StringComparison.OrdinalIgnoreCase)
                || link.CapacityMw != topologyLink.CapacityMw)
            {
                return "System dispatch interconnector evidence does not match declared topology.";
            }

            var direction = (link.FromRegionId.ToUpperInvariant(), link.ToRegionId.ToUpperInvariant());
            if (!directions.Add(direction))
            {
                return "System dispatch interconnector evidence contains a duplicate link.";
            }

            if (!double.IsFinite(link.CapacityMw)
                || link.CapacityMw < 0)
            {
                return "System dispatch interconnector capacity must be finite and non-negative.";
            }

            if (link.FlowMw is null
                || link.LossesMw is null
                || link.FlowMw.Length != intervalCount
                || link.LossesMw.Length != intervalCount)
            {
                return "System dispatch interconnector series must align with the dispatch intervals.";
            }

            for (int interval = 0; interval < intervalCount; interval++)
            {
                double flow = link.FlowMw[interval];
                double losses = link.LossesMw[interval];
                if (!double.IsFinite(flow)
                    || !double.IsFinite(losses)
                    || flow < 0
                    || losses < 0)
                {
                    return "System dispatch interconnector series must be finite and non-negative.";
                }

                if (flow > link.CapacityMw + FlowToleranceMw
                    || losses > flow + FlowToleranceMw)
                {
                    return "System dispatch interconnector flow exceeds its declared capacity or loss ledger.";
                }
            }
        }

        // Published *Mw fields are independently rounded to 1 decimal place (JsonFile.DecimalPlaces),
        // so summing several links' rounded losses can drift from the system total's own rounding by
        // up to half a published unit per term.
        double lossLedgerTolerance = (0.05 * (result.Interconnectors.Length + 1)) + FlowToleranceMw;
        for (int interval = 0; interval < intervalCount; interval++)
        {
            double linkLosses = result.Interconnectors.Sum(link => link.LossesMw[interval]);
            if (Math.Abs(result.DataSeries.TransmissionLossesMw[interval] - linkLosses) > lossLedgerTolerance)
            {
                return "System transmission-loss series does not match its interconnector loss ledger.";
            }
        }

        return null;
    }

    private static string? ValidateRegionsAndTopology(
        string[]? regionIds,
        Dictionary<string, RegionDispatchSummaryDTO>? regionSummariesById,
        DispatchTopologyDTO? topology)
    {
        if (regionIds is null
            || regionIds.Length == 0
            || regionIds.Any(string.IsNullOrWhiteSpace)
            || regionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != regionIds.Length)
        {
            return "System dispatch region IDs are missing or duplicated.";
        }

        if (regionSummariesById is null
            || !regionIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(regionSummariesById.Keys)
            || regionSummariesById.Values.Any(summary =>
                summary is null
                || summary.DeliveredGenerationByTechnologyMwh is null
                || summary.DeliveredGenerationByTechnologyMwh.Any(entry =>
                    string.IsNullOrWhiteSpace(entry.Key)
                    || !double.IsFinite(entry.Value)
                    || entry.Value < 0)
                || Math.Abs(
                    summary.DeliveredGenerationByTechnologyMwh.Values.Sum()
                    - summary.Metrics.DeliveredGenerationMwh) > ArtifactEnergyToleranceMwh))
        {
            return "System dispatch regional generation totals are invalid.";
        }

        return ValidateTopology(topology, new HashSet<string>(regionIds, StringComparer.OrdinalIgnoreCase));
    }

    private static string? ValidateTopology(
        DispatchTopologyDTO? topology,
        HashSet<string> systemRegionIds)
    {
        if (topology?.RegionIds is null
            || topology.Links is null
            || topology.RegionIds.Length != systemRegionIds.Count
            || topology.RegionIds.Any(string.IsNullOrWhiteSpace)
            || !topology.RegionIds.All(systemRegionIds.Contains))
        {
            return "System dispatch topology regions do not match its regions.";
        }

        var linkIds = new HashSet<string>(StringComparer.Ordinal);
        var directions = new HashSet<(string From, string To)>();
        foreach (DispatchTopologyLinkDTO? link in topology.Links)
        {
            if (link is null
                || string.IsNullOrWhiteSpace(link.Id)
                || string.IsNullOrWhiteSpace(link.FromRegionId)
                || string.IsNullOrWhiteSpace(link.ToRegionId)
                || string.Equals(link.FromRegionId, link.ToRegionId, StringComparison.OrdinalIgnoreCase)
                || !systemRegionIds.Contains(link.FromRegionId)
                || !systemRegionIds.Contains(link.ToRegionId)
                || !double.IsFinite(link.CapacityMw)
                || link.CapacityMw < 0
                || !linkIds.Add(link.Id)
                || !directions.Add((link.FromRegionId.ToUpperInvariant(), link.ToRegionId.ToUpperInvariant())))
            {
                return "System dispatch topology links are invalid.";
            }
        }

        return null;
    }

    private static string? ValidateSeriesAndCost(DispatchSeriesDTO? series, DispatchCostDTO? cost)
    {
        if (series?.Demand?.TotalDemandMw is null
            || series.ImportsMw is null
            || series.ExportsMw is null
            || series.TransmissionLossesMw is null)
        {
            return "Dispatch transmission series are missing.";
        }

        int intervalCount = series.Demand.TotalDemandMw.Length;
        if (series.ImportsMw.Length != intervalCount
            || series.ExportsMw.Length != intervalCount
            || series.TransmissionLossesMw.Length != intervalCount)
        {
            return "Dispatch transmission series are misaligned.";
        }

        if (!AllFiniteAndNonNegative(series.ImportsMw)
            || !AllFiniteAndNonNegative(series.ExportsMw)
            || !AllFiniteAndNonNegative(series.TransmissionLossesMw))
        {
            return "Dispatch transmission series must be finite and non-negative.";
        }

        return ValidateCost(cost);
    }

    private static string? ValidateCost(DispatchCostDTO? cost)
    {
        if (cost is null
            || cost.AnnualisedTransmissionCostAud < 0
            || cost.TransmissionSlcotAudPerMwh < 0
            || !Enum.IsDefined(cost.TransmissionCostStatus)
            || (cost.TransmissionCostStatus == TransmissionCostStatus.NotModelled
                && (cost.AnnualisedTransmissionCostAud != 0
                    || cost.TransmissionSlcotAudPerMwh != 0))
            || !double.IsFinite(cost.NetImportedEnergyMwh))
        {
            return "Dispatch transmission cost evidence is invalid.";
        }

        if (cost.GenerationCostContributions is null
            || cost.GenerationCostContributions.Any(contribution => contribution is null
                || string.IsNullOrWhiteSpace(contribution.Technology)
                || contribution.AnnualisedCostAud < 0
                || contribution.LevelisedContributionAudPerMwh < 0)
            || cost.GenerationCostContributions
                .Select(contribution => contribution.Technology)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != cost.GenerationCostContributions.Length)
        {
            return "Dispatch generation cost contributions are invalid.";
        }

        decimal annualisedContributionCost = cost.GenerationCostContributions
            .Sum(contribution => contribution.AnnualisedCostAud);
        if (annualisedContributionCost != cost.AnnualisedGenerationCostAud)
        {
            return "Dispatch generation cost contributions do not reconcile to annualised generation cost.";
        }

        return null;
    }

    private static bool AllFiniteAndNonNegative(IEnumerable<double> values) =>
        values.All(value => double.IsFinite(value) && value >= 0);
}