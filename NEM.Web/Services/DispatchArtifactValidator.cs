using NEM.Contracts;

namespace NEM.Web.Services;

/// <summary>Validates dispatch evidence that is required by the supported artifact schemas.</summary>
public static class DispatchArtifactValidator
{
    private const double FlowToleranceMw = 1e-9;

    public static string? Validate(object artifact) => artifact switch
    {
        DispatchResultsDTO result => Validate(result),
        SystemDispatchResultsDTO result => Validate(result),
        RegionDispatchResultsDTO result => Validate(result),
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

    public static string? Validate(SystemDispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string? commonValidation = ValidateSeriesAndCost(result.DataSeries, result.Cost);
        if (commonValidation is not null)
        {
            return commonValidation;
        }

        if (result.RegionIds is null
            || result.RegionIds.Length == 0
            || result.RegionIds.Any(string.IsNullOrWhiteSpace)
            || result.RegionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.RegionIds.Length)
        {
            return "System dispatch region IDs are missing or duplicated.";
        }

        if (result.Interconnectors is null)
        {
            return "System dispatch interconnector evidence is missing.";
        }

        int intervalCount = result.DataSeries.Demand.TotalDemandMw.Length;
        var regionIds = new HashSet<string>(result.RegionIds, StringComparer.OrdinalIgnoreCase);
        var directions = new HashSet<(string From, string To)>();
        for (int linkIndex = 0; linkIndex < result.Interconnectors.Length; linkIndex++)
        {
            DispatchInterconnectorDTO? link = result.Interconnectors[linkIndex];
            if (link is null
                || string.IsNullOrWhiteSpace(link.FromRegionId)
                || string.IsNullOrWhiteSpace(link.ToRegionId)
                || string.Equals(link.FromRegionId, link.ToRegionId, StringComparison.OrdinalIgnoreCase)
                || !regionIds.Contains(link.FromRegionId)
                || !regionIds.Contains(link.ToRegionId))
            {
                return "System dispatch interconnector endpoints do not match its regions.";
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

        for (int interval = 0; interval < intervalCount; interval++)
        {
            double linkLosses = result.Interconnectors.Sum(link => link.LossesMw[interval]);
            if (Math.Abs(result.DataSeries.TransmissionLossesMw[interval] - linkLosses) > FlowToleranceMw)
            {
                return "System transmission-loss series does not match its interconnector loss ledger.";
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

        if (cost is null
            || cost.AnnualisedTransmissionCostAud < 0
            || cost.TransmissionSlcotAudPerMwh < 0
            || !double.IsFinite(cost.NetImportedEnergyMwh))
        {
            return "Dispatch transmission cost evidence is invalid.";
        }

        return null;
    }

    private static bool AllFiniteAndNonNegative(IEnumerable<double> values) =>
        values.All(value => double.IsFinite(value) && value >= 0);
}