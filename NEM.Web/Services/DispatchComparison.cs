using NEM.Contracts;

namespace NEM.Web.Services;

/// <summary>
/// A second sweep point drawn against the primary one. A sweep is a comparative object, so the
/// run page can hold two runs at once; they are only comparable when they share a period,
/// resolution and interval count.
/// </summary>
public sealed record DispatchComparison(
    SweepIndexPointDTO Point,
    DispatchResultsDTO Result);

public static class DispatchComparisonValidator
{
    /// <summary>
    /// Returns null when the two runs may share axes, or the reason they may not.
    /// </summary>
    public static string? Validate(DispatchResultsDTO primary, DispatchResultsDTO comparison)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(comparison);

        if (primary.Scenario.PeriodStart != comparison.Scenario.PeriodStart
            || primary.Scenario.PeriodEnd != comparison.Scenario.PeriodEnd)
        {
            return "The compared run covers a different period and cannot share these axes.";
        }

        if (primary.Scenario.Resolution != comparison.Scenario.Resolution)
        {
            return "The compared run uses a different resolution and cannot share these axes.";
        }

        if (primary.Scenario.Region != comparison.Scenario.Region)
        {
            return "The compared run covers a different region and cannot share these axes.";
        }

        return primary.DataSeries.Demand.TotalDemandMw.Length != comparison.DataSeries.Demand.TotalDemandMw.Length
            ? "The compared run has a different number of intervals and cannot share these axes."
            : null;
    }
}
