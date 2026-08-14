using System.Globalization;
using System.Text.Json;
using NEM.Contracts;

namespace NEM.Web.Components;

public sealed record SweepChartYAxis(
    string Key,
    string Label,
    string Unit,
    Func<SweepPointScalarResultsDTO, double?> SelectValue,
    /// <summary>Symbol placed before the value, for series measured in money.</summary>
    string? ValuePrefix = null);

public sealed record SweepChartPoint(
    SweepIndexPointDTO Point,
    double Value);

/// <summary>
/// A run that is not on the chart, and why. A failed run is a result — the scenario reached a
/// constraint — so it is named rather than dropped, and its stage is kept so failures group.
/// </summary>
public sealed record SweepChartOmittedPoint(
    string Label,
    double AxisValue,
    string Reason,
    SweepFailureStage? Stage = null,
    string? Code = null);

public sealed record SweepChartData(
    string[] Labels,
    double[] Values,
    IReadOnlyList<SweepChartPoint> Points,
    IReadOnlyList<SweepChartOmittedPoint> OmittedPoints)
{
    public static SweepChartData Build(
        SweepIndexDTO index,
        SweepChartYAxis yAxis,
        string? regionId = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(yAxis);

        var points = new List<SweepChartPoint>();
        var omittedPoints = new List<SweepChartOmittedPoint>();

        foreach (SweepIndexPointDTO point in index.Points)
        {
            if (point.Status == SweepPointStatus.Failed)
            {
                omittedPoints.Add(new SweepChartOmittedPoint(
                    point.Label,
                    point.AxisValue,
                    point.Failure?.Message ?? "This run did not produce results.",
                    point.Failure?.Stage,
                    point.Failure?.Code));
                continue;
            }

            SweepPointScalarResultsDTO? scalars = SelectScalars(point, regionId);
            double? value = scalars is null ? null : yAxis.SelectValue(scalars);
            if (value is null)
            {
                // A succeeded point the artifact carries no value for is still a gap in the
                // evidence, so it is surfaced rather than quietly missing from the series.
                omittedPoints.Add(new SweepChartOmittedPoint(
                    point.Label,
                    point.AxisValue,
                    $"The artifact carries no {yAxis.Label.ToLowerInvariant()} value for this run."));
                continue;
            }

            points.Add(new SweepChartPoint(point, value.Value));
        }

        return new SweepChartData(
            points.Select(point => point.Point.AxisValue.ToString("G", CultureInfo.InvariantCulture)).ToArray(),
            points.Select(point => point.Value).ToArray(),
            points,
            omittedPoints);
    }

    public static SweepPointScalarResultsDTO? SelectScalars(
        SweepIndexPointDTO point,
        string? regionId = null)
    {
        if (string.IsNullOrWhiteSpace(regionId))
        {
            return point.Scalars;
        }

        return point.RegionScalars?
            .FirstOrDefault(region => string.Equals(
                region.RegionId,
                regionId,
                StringComparison.OrdinalIgnoreCase))
            ?.Scalars;
    }
}

/// <summary>
/// Joins <see cref="SweepScalarCatalog"/> — which owns every scalar's display label, unit and
/// currency — to the accessor that reads it off a point. The contract supplies the metadata; only
/// the accessor is a client concern, and a guard test fails if a scalar gains no accessor here.
/// </summary>
public static class SweepSeriesCatalogue
{
    private static readonly IReadOnlyDictionary<string, Func<SweepPointScalarResultsDTO, double?>> Accessors =
        new Dictionary<string, Func<SweepPointScalarResultsDTO, double?>>(StringComparer.Ordinal)
        {
            ["slcoeAudPerMwh"] = scalars => (double)scalars.SlcoeAudPerMwh,
            ["generationSlcoeAudPerMwh"] = scalars => (double)scalars.GenerationSlcoeAudPerMwh,
            ["storageSlcoeAudPerMwh"] = scalars => (double)scalars.StorageSlcoeAudPerMwh,
            ["demandMwh"] = scalars => scalars.DemandMwh,
            ["energyServedMwh"] = scalars => scalars.EnergyServedMwh,
            ["deliveredGenerationMwh"] = scalars => scalars.DeliveredGenerationMwh,
            ["achievedRenewableShareGridScale"] = scalars => scalars.AchievedRenewableShareGridScale,
            ["achievedRenewableShareNative"] = scalars => scalars.AchievedRenewableShareNative,
            ["storagePowerMw"] = scalars => scalars.StoragePowerMw,
            ["storageEnergyMwh"] = scalars => scalars.StorageEnergyMwh,
            ["unservedEnergyMwh"] = scalars => scalars.UnservedEnergyMwh,
            ["unservedEnergyPercentageOfDemand"] = scalars => scalars.UnservedEnergyPercentageOfDemand,
            ["unservedHours"] = scalars => scalars.UnservedHours,
            ["hoursServedFraction"] = scalars => scalars.HoursServedFraction,
            ["peakUnservedPowerMw"] = scalars => scalars.PeakUnservedPowerMw,
            ["curtailedEnergyMwh"] = scalars => scalars.CurtailedEnergyMwh,
            ["transmissionSlcotAudPerMwh"] = scalars => (double)scalars.TransmissionSlcotAudPerMwh,
            ["netImportedEnergyMwh"] = scalars => scalars.NetImportedEnergyMwh,
        };

    private static readonly IReadOnlyDictionary<string, SweepChartYAxis> ByKey =
        SweepScalarCatalog.Descriptors
            .Where(descriptor => Accessors.ContainsKey(descriptor.Name))
            .ToDictionary(
                descriptor => descriptor.Name,
                descriptor => new SweepChartYAxis(
                    descriptor.Name,
                    descriptor.Label,
                    descriptor.Unit,
                    Accessors[descriptor.Name],
                    descriptor.Currency is null ? null : "$"),
                StringComparer.OrdinalIgnoreCase);

    public static string SupportedKeys => string.Join(", ", ByKey.Keys.Order(StringComparer.Ordinal));

    /// <summary>Every chartable scalar, in the order the contract declares them.</summary>
    public static IReadOnlyList<SweepChartYAxis> All { get; } =
        [.. SweepScalarCatalog.Descriptors
            .Where(descriptor => ByKey.ContainsKey(descriptor.Name))
            .Select(descriptor => ByKey[descriptor.Name])];

    /// <summary>Scalar names the contract declares that this client cannot read. Empty in a healthy build.</summary>
    public static IReadOnlyList<string> UnmappedScalarNames { get; } =
        [.. SweepScalarCatalog.ScalarNames().Where(name => !Accessors.ContainsKey(name))];

    public static SweepChartYAxis? Resolve(string? key) =>
        key is not null && ByKey.TryGetValue(key, out SweepChartYAxis? axis) ? axis : null;
}

/// <summary>
/// Sweep scalars span cents per MWh and tens of millions of MWh, and a share of demand can be
/// three decimal places from zero. One rounding rule would either lose the small values or
/// clutter the large ones, so precision follows magnitude.
/// </summary>
public static class SweepValueFormat
{
    public static string Short(double value)
    {
        if (value == Math.Round(value) && Math.Abs(value) < 1e15)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        return Math.Abs(value) switch
        {
            >= 1000 => value.ToString("N0", CultureInfo.CurrentCulture),
            >= 0.01 => value.ToString("N2", CultureInfo.CurrentCulture),
            _ => value.ToString("G3", CultureInfo.CurrentCulture),
        };
    }

    /// <summary>
    /// Places a currency symbol before the digits, keeping any minus sign outermost so a negative
    /// reads as -$5.00 rather than $-5.00.
    /// </summary>
    public static string WithPrefix(string? prefix, double value, string formatted)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return formatted;
        }

        return value < 0
            ? $"-{prefix}{formatted.TrimStart('-')}"
            : $"{prefix}{formatted}";
    }

    public static string Short(double value, string? prefix) =>
        WithPrefix(prefix, value, Short(value));

    /// <summary>
    /// A numeric format holding enough decimals for every value in a table column. A column reads
    /// as a column only when its cells share a decimal count, so precision is chosen once from the
    /// whole column rather than per cell.
    /// </summary>
    public static string ColumnFormat(IEnumerable<double?> values, int maximumDecimals = 8)
    {
        ArgumentNullException.ThrowIfNull(values);

        int decimals = 0;
        foreach (double? value in values)
        {
            if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            {
                continue;
            }

            for (int candidate = decimals; candidate <= maximumDecimals; candidate++)
            {
                double tolerance = 1e-12 * Math.Max(1, Math.Abs(value.Value));
                if (Math.Abs(Math.Round(value.Value, candidate) - value.Value) <= tolerance)
                {
                    decimals = candidate;
                    break;
                }

                decimals = maximumDecimals;
            }
        }

        return $"N{decimals}";
    }
}

/// <summary>
/// Chooses the y-axis tick interval from the data. MudBlazor's interval is an integer and its
/// default choice is poor for these ranges: a series running from 2.53 to 3.19 was drawn against
/// an axis labelled 0 and 20, and one running to 83 million was labelled in multiples of 1,310,720.
/// The axis is anchored at zero so a small absolute change is not magnified into a steep line.
/// </summary>
public static class SweepChartScale
{
    private static readonly double[] NiceSteps = [1, 2, 2.5, 5, 10];

    public static int TickInterval(double maximum, int targetIntervals = 5)
    {
        if (maximum <= 0 || double.IsNaN(maximum) || double.IsInfinity(maximum))
        {
            return 1;
        }

        double raw = maximum / targetIntervals;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1))));
        foreach (double step in NiceSteps)
        {
            double candidate = step * magnitude;
            if (candidate >= raw)
            {
                return (int)Math.Max(1, Math.Round(candidate));
            }
        }

        return (int)Math.Max(1, Math.Round(10 * magnitude));
    }
}
