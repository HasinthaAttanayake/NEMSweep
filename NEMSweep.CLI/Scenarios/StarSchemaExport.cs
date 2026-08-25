using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Scenarios;

/// <summary>
/// Writes a run as a star schema of CSV tables: narrow fact tables at the hour grain joined to small
/// dimension tables. JSON is the contract every artifact is defined by, but it is not something a
/// person can open, and the shape a spreadsheet wants is not the shape an analytical tool wants.
/// Tidy facts plus dimensions serve both, so this is one export rather than a format flag.
/// </summary>
/// <remarks>
/// Technology is unpivoted because it is a real dimension: that is what lets a consumer filter by it
/// without the schema changing when a sixth technology appears. Scalars stay wide, because their
/// units are heterogeneous and collapsing AUD/MWh, MWh, fractions and hours into one value column is
/// a units error waiting to happen. It also keeps <c>fact_scalars.csv</c> small enough to open in a
/// spreadsheet, which is the one table a reader is most likely to want.
/// </remarks>
internal static class StarSchemaExport
{
    /// <summary>Hour keys start at 1, so a reader never has to guess whether the axis is zero-based.</summary>
    private const int FirstHourIndex = 1;

    /// <summary>
    /// Writes every table for one published run.
    /// </summary>
    /// <param name="publication">The run's system and per-region results.</param>
    /// <param name="directory">Directory the tables are written to; created if absent.</param>
    /// <param name="pointId">
    /// Identifier for this run within a study. A sweep passes its point id; a single scenario passes
    /// its scenario id. Present on every fact row either way, so a folder of runs concatenates into
    /// one table without a consumer having to reconstruct which run a row came from.
    /// </param>
    /// <param name="writeText">Injection point for tests; defaults to writing the file.</param>
    public static void Write(
        DispatchPublication publication,
        string directory,
        string pointId,
        Action<string, string>? writeText = null)
    {
        WriteDimensions(publication, directory, writeText);
        WriteFacts(publication, directory, pointId, writeText);
    }

    /// <summary>
    /// Writes the dimension tables alone. A sweep calls this once for the whole study rather than
    /// per point: dimensions are shared by construction, and the calendar repeated twenty-five times
    /// would be megabytes of identical rows.
    /// </summary>
    /// <param name="publication">Any run of the study; dimensions are the same across its points.</param>
    /// <param name="directory">Directory the tables are written to; created if absent.</param>
    /// <param name="writeText">Injection point for tests; defaults to writing the file.</param>
    public static void WriteDimensions(
        DispatchPublication publication,
        string directory,
        Action<string, string>? writeText = null)
    {
        ArgumentNullException.ThrowIfNull(publication);
        writeText ??= File.WriteAllText;
        Directory.CreateDirectory(directory);

        SystemDispatchResultsDTO system = publication.System;
        Write(writeText, directory, "dim_time.csv", TimeDimension(system));
        Write(writeText, directory, "dim_region.csv", RegionDimension(system));
        Write(writeText, directory, "dim_technology.csv", TechnologyDimension());
        Write(writeText, directory, "dim_scalar.csv", ScalarDimension());
    }

    /// <summary>Writes the fact tables for one run.</summary>
    /// <param name="publication">The run's system and per-region results.</param>
    /// <param name="directory">Directory the tables are written to; created if absent.</param>
    /// <param name="pointId">Identifier stamped on every fact row.</param>
    /// <param name="writeText">Injection point for tests; defaults to writing the file.</param>
    public static void WriteFacts(
        DispatchPublication publication,
        string directory,
        string pointId,
        Action<string, string>? writeText = null)
    {
        ArgumentNullException.ThrowIfNull(publication);
        writeText ??= File.WriteAllText;
        Directory.CreateDirectory(directory);

        SystemDispatchResultsDTO system = publication.System;
        IReadOnlyDictionary<string, RegionDispatchResultsDTO> regions = publication.Regions;

        Write(writeText, directory, "fact_dispatch.csv", DispatchFacts(pointId, regions));
        Write(writeText, directory, "fact_generation.csv", GenerationFacts(pointId, regions));
        Write(writeText, directory, "fact_storage.csv", StorageFacts(pointId, regions));
        Write(writeText, directory, "fact_interconnector.csv", InterconnectorFacts(pointId, system));
        Write(writeText, directory, "fact_scalars.csv", ScalarFacts(pointId, system, regions));
    }

    /// <summary>
    /// The point dimension for a sweep: the axis value and outcome each set of facts belongs to, so a
    /// consumer can label and order points without parsing the sweep index.
    /// </summary>
    /// <param name="points">The sweep's published index entries.</param>
    /// <param name="directory">Directory the table is written to; created if absent.</param>
    /// <param name="writeText">Injection point for tests; defaults to writing the file.</param>
    public static void WritePointDimension(
        IEnumerable<SweepIndexPointDTO> points,
        string directory,
        Action<string, string>? writeText = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        writeText ??= File.WriteAllText;
        Directory.CreateDirectory(directory);
        Write(writeText, directory, "dim_point.csv", PointDimension(points));
    }

    private static IEnumerable<string[]> PointDimension(IEnumerable<SweepIndexPointDTO> points)
    {
        yield return ["pointId", "label", "axisValue", "status", "storageSizingOutcome"];
        foreach (SweepIndexPointDTO point in points)
        {
            yield return
            [
                point.PointId,
                point.Label,
                Text(point.AxisValue, 6),
                point.Status.ToString(),
                point.StorageSizing?.Outcome.ToString() ?? string.Empty,
            ];
        }
    }

    /// <summary>
    /// The calendar, one row per hour of the modelled year. Keyed on an integer rather than the
    /// timestamp because spreadsheets reinterpret ISO 8601 by locale on import, which silently
    /// swaps day and month on an Australian machine; the timestamp rides alongside it.
    /// </summary>
    private static IEnumerable<string[]> TimeDimension(SystemDispatchResultsDTO system)
    {
        yield return
        [
            "hourIndex", "timestamp", "date", "hourOfDay", "month", "monthName", "quarter",
            "financialYear", "dayOfWeek", "isWeekend",
        ];

        int hours = HourCount(system);
        for (int index = 0; index < hours; index++)
        {
            DateTimeOffset at = system.PeriodStart + system.Resolution * index;
            yield return
            [
                Text(index + FirstHourIndex),
                at.ToString("o", CultureInfo.InvariantCulture),
                at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Text(at.Hour),
                Text(at.Month),
                at.ToString("MMMM", CultureInfo.InvariantCulture),
                Text(((at.Month - 1) / 3) + 1),
                FinancialYear(at),
                at.DayOfWeek.ToString(),
                Text(at.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday),
            ];
        }
    }

    /// <summary>
    /// Regions and where they sit. Coordinates are lifted from the interconnector endpoints, which
    /// already carry each region's solar weather site, so a consumer gets map placement without the
    /// model publishing a second copy of the same fact.
    /// </summary>
    private static IEnumerable<string[]> RegionDimension(SystemDispatchResultsDTO system)
    {
        yield return ["regionId", "latitude", "longitude"];

        var located = new Dictionary<string, (double Latitude, double Longitude)>(StringComparer.Ordinal);
        foreach (DispatchInterconnectorDTO link in system.Interconnectors)
        {
            located.TryAdd(link.FromRegionId, (link.FromLatitude, link.FromLongitude));
            located.TryAdd(link.ToRegionId, (link.ToLatitude, link.ToLongitude));
        }

        foreach (string regionId in system.RegionIds)
        {
            bool known = located.TryGetValue(regionId, out (double Latitude, double Longitude) at);
            yield return
            [
                regionId,
                known ? Text(at.Latitude, 4) : string.Empty,
                known ? Text(at.Longitude, 4) : string.Empty,
            ];
        }
    }

    /// <summary>The technology vocabulary, so a consumer can colour and group without a lookup of its own.</summary>
    private static IEnumerable<string[]> TechnologyDimension()
    {
        yield return ["technology", "category", "isRenewable"];
        yield return ["Solar", "generation", "true"];
        yield return ["Wind", "generation", "true"];
        yield return ["Hydro", "generation", "true"];
        yield return ["Coal", "generation", "false"];
        yield return ["Gas", "generation", "false"];
        yield return ["Battery", "storage", "false"];
        yield return ["PumpedHydro", "storage", "false"];
    }

    /// <summary>
    /// Labels and units for the scalar columns, taken from the published catalogue rather than
    /// restated, so the two cannot drift.
    /// </summary>
    private static IEnumerable<string[]> ScalarDimension()
    {
        yield return ["scalarName", "label", "unit", "chartable"];
        foreach (SweepScalarDescriptor descriptor in SweepScalarCatalog.Descriptors)
        {
            yield return
            [
                descriptor.Name,
                descriptor.Label,
                descriptor.Unit,
                Text(descriptor.Chartable),
            ];
        }
    }

    /// <summary>Measures that are one series per region, kept wide because each is a different quantity.</summary>
    private static IEnumerable<string[]> DispatchFacts(
        string pointId,
        IReadOnlyDictionary<string, RegionDispatchResultsDTO> regions)
    {
        yield return
        [
            "pointId", "regionId", "hourIndex", "totalDemandMw", "baseDemandMw", "curtailmentMw",
            "unservedDemandMw", "chargeMw", "dischargeMw", "importsMw", "exportsMw",
            "transmissionLossesMw",
        ];

        foreach ((string regionId, RegionDispatchResultsDTO region) in Ordered(regions))
        {
            DispatchSeriesDTO series = region.DataSeries;
            int hours = series.Demand.TotalDemandMw.Length;
            for (int index = 0; index < hours; index++)
            {
                yield return
                [
                    pointId,
                    regionId,
                    Text(index + FirstHourIndex),
                    Power(series.Demand.TotalDemandMw, index),
                    Power(series.Demand.BaseDemandMw, index),
                    Power(series.CurtailmentMw, index),
                    Power(series.UnservedDemandMw, index),
                    Power(series.ChargeMw, index),
                    Power(series.DischargeMw, index),
                    Power(series.ImportsMw, index),
                    Power(series.ExportsMw, index),
                    Power(series.TransmissionLossesMw, index),
                ];
            }
        }
    }

    /// <summary>Delivered generation, with technology as a dimension rather than a column per technology.</summary>
    private static IEnumerable<string[]> GenerationFacts(
        string pointId,
        IReadOnlyDictionary<string, RegionDispatchResultsDTO> regions) =>
        TechnologyFacts(
            pointId,
            regions,
            "deliveredMw",
            region => region.DataSeries.DeliveredGenerationByTechnologyMw);

    /// <summary>Interval-beginning state of charge, likewise unpivoted by technology.</summary>
    private static IEnumerable<string[]> StorageFacts(
        string pointId,
        IReadOnlyDictionary<string, RegionDispatchResultsDTO> regions) =>
        TechnologyFacts(
            pointId,
            regions,
            "stateOfChargeMwh",
            region => region.DataSeries.StateOfChargeByTechnologyMwh);

    private static IEnumerable<string[]> TechnologyFacts(
        string pointId,
        IReadOnlyDictionary<string, RegionDispatchResultsDTO> regions,
        string valueColumn,
        Func<RegionDispatchResultsDTO, Dictionary<string, double[]>> select)
    {
        yield return ["pointId", "regionId", "hourIndex", "technology", valueColumn];

        foreach ((string regionId, RegionDispatchResultsDTO region) in Ordered(regions))
        {
            foreach ((string technology, double[] values) in
                select(region).OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                for (int index = 0; index < values.Length; index++)
                {
                    yield return
                    [
                        pointId,
                        regionId,
                        Text(index + FirstHourIndex),
                        technology,
                        Power(values, index),
                    ];
                }
            }
        }
    }

    /// <summary>Directed link flows and losses, at the same hour grain as the regional facts.</summary>
    private static IEnumerable<string[]> InterconnectorFacts(
        string pointId,
        SystemDispatchResultsDTO system)
    {
        yield return
        [
            "pointId", "linkId", "fromRegionId", "toRegionId", "hourIndex", "flowMw", "lossesMw",
            "capacityMw", "distanceKm",
        ];

        foreach (DispatchInterconnectorDTO link in
            system.Interconnectors.OrderBy(link => link.Id, StringComparer.Ordinal))
        {
            for (int index = 0; index < link.FlowMw.Length; index++)
            {
                yield return
                [
                    pointId,
                    link.Id,
                    link.FromRegionId,
                    link.ToRegionId,
                    Text(index + FirstHourIndex),
                    Power(link.FlowMw, index),
                    Power(link.LossesMw, index),
                    Text(link.CapacityMw, 1),
                    Text(link.DistanceKm, 1),
                ];
            }
        }
    }

    /// <summary>
    /// One row per scope: each region, then the system as a whole. Column order follows the
    /// catalogue, and each value is rounded by the same rule the JSON writer applies, so a figure
    /// read here matches the one read there.
    /// </summary>
    private static IEnumerable<string[]> ScalarFacts(
        string pointId,
        SystemDispatchResultsDTO system,
        IReadOnlyDictionary<string, RegionDispatchResultsDTO> regions)
    {
        string[] names = [.. SweepScalarCatalog.Descriptors.Select(descriptor => descriptor.Name)];
        yield return ["pointId", "scope", .. names];

        foreach ((string regionId, RegionDispatchResultsDTO region) in Ordered(regions))
        {
            yield return [pointId, regionId, .. ScalarValues(SweepArtifactExport.CreateScalars(region), names)];
        }

        yield return [pointId, "SYSTEM", .. ScalarValues(SweepArtifactExport.CreateScalars(system), names)];
    }

    /// <summary>
    /// Reads the scalar record by the names the catalogue publishes, so a scalar added to the record
    /// appears here without this method being touched.
    /// </summary>
    private static IEnumerable<string> ScalarValues(SweepPointScalarResultsDTO scalars, string[] names)
    {
        PropertyInfo[] properties = typeof(SweepPointScalarResultsDTO).GetProperties();
        foreach (string name in names)
        {
            PropertyInfo? property = Array.Find(
                properties,
                candidate => string.Equals(
                    JsonNamingPolicy.CamelCase.ConvertName(candidate.Name),
                    name,
                    StringComparison.Ordinal));
            object? value = property?.GetValue(scalars);
            yield return value switch
            {
                null => string.Empty,
                double number => Text(number, JsonFile.DecimalPlaces(name)),
                decimal money => Text((double)money, JsonFile.DecimalPlaces(name)),
                int count => Text(count),
                _ => value.ToString() ?? string.Empty,
            };
        }
    }

    /// <summary>
    /// Regions in a stable order, keyed by region identifier. The publication dictionary is keyed by
    /// file name, which is a publishing detail rather than something a fact table should carry.
    /// </summary>
    private static IEnumerable<(string RegionId, RegionDispatchResultsDTO Region)> Ordered(
        IReadOnlyDictionary<string, RegionDispatchResultsDTO> regions) =>
        regions.Values
            .OrderBy(region => region.RegionId, StringComparer.Ordinal)
            .Select(region => (region.RegionId, region));

    private static int HourCount(SystemDispatchResultsDTO system) =>
        system.DataSeries.Demand.TotalDemandMw.Length;

    /// <summary>Australian financial year, which is how demand and generation data is published.</summary>
    private static string FinancialYear(DateTimeOffset at) =>
        at.Month >= 7
            ? $"FY{at.Year + 1}"
            : $"FY{at.Year}";

    private static string Power(double[]? series, int index) =>
        series is null || index >= series.Length ? string.Empty : Text(series[index], 1);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(bool value) => value ? "true" : "false";

    private static string Text(double value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero)
            .ToString("0." + new string('#', Math.Max(decimals, 1)), CultureInfo.InvariantCulture);

    private static void Write(
        Action<string, string> writeText,
        string directory,
        string fileName,
        IEnumerable<string[]> rows)
    {
        var text = new StringBuilder();
        foreach (string[] row in rows)
        {
            for (int column = 0; column < row.Length; column++)
            {
                if (column > 0)
                {
                    text.Append(',');
                }

                text.Append(Escape(row[column]));
            }

            text.Append('\n');
        }

        writeText(Path.Combine(directory, fileName), text.ToString());
    }

    /// <summary>Quotes only where a value would otherwise break the row, keeping the files diffable.</summary>
    private static string Escape(string value) =>
        value.AsSpan().IndexOfAny(",\"\n\r") >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
}
