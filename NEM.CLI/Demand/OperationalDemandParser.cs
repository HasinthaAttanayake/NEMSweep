using CsvHelper;
using CsvHelper.Configuration;
using NEM.Model.Series;
using System.Globalization;
using System.IO.Compression;

namespace NEM.CLI.Demand;

internal sealed record OperationalDemandData(
    string Region,
    FlowSeries Demand,
    IReadOnlyList<string> SourceArchives,
    int ClampedIntervals = 0);

internal sealed class OperationalDemandDataQualityException : Exception
{
    public OperationalDemandDataQualityException(string message)
        : base(message)
    {
    }
}

internal static class OperationalDemandParser
{
    internal static readonly TimeSpan Resolution = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);
    private static readonly CsvConfiguration CsvConfiguration = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = false,
        IgnoreBlankLines = true,
        TrimOptions = TrimOptions.Trim,
    };

    public static IReadOnlyDictionary<string, OperationalDemandData> Read(
        IReadOnlyList<string> archivePaths,
        IReadOnlyCollection<string> regionIds,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        ArgumentNullException.ThrowIfNull(archivePaths);
        ArgumentNullException.ThrowIfNull(regionIds);
        if (regionIds.Count == 0)
        {
            throw new ArgumentException("At least one region must be requested.", nameof(regionIds));
        }

        if (periodStart.Offset != NemOffset)
        {
            throw new ArgumentException(
                "The operational-demand period start must use NEM market time (UTC+10).",
                nameof(periodStart));
        }

        if (periodEnd.Offset != NemOffset)
        {
            throw new ArgumentException(
                "The operational-demand period end must use NEM market time (UTC+10).",
                nameof(periodEnd));
        }

        if (periodEnd <= periodStart)
        {
            throw new ArgumentException(
                "The operational-demand period end must be after the period start.",
                nameof(periodEnd));
        }

        long slotTicks = periodEnd.Subtract(periodStart).Ticks;
        if (slotTicks % Resolution.Ticks != 0)
        {
            throw new ArgumentException(
                "The operational-demand period must contain complete 30-minute intervals.",
                nameof(periodEnd));
        }

        string[] requestedRegions = regionIds
            .Select(region => region?.Trim() ?? string.Empty)
            .ToArray();
        if (requestedRegions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Requested region IDs must not be blank.", nameof(regionIds));
        }

        var valuesByRegion = requestedRegions.ToDictionary(
            region => region,
            _ => new Dictionary<DateTimeOffset, DemandValue>(),
            StringComparer.OrdinalIgnoreCase);
        string[] sourceArchives = archivePaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceArchives.Length == 0)
        {
            throw new OperationalDemandDataQualityException("No operational-demand archives were provided.");
        }

        foreach (string sourceArchive in sourceArchives)
        {
            if (!File.Exists(sourceArchive))
            {
                throw new FileNotFoundException("Operational-demand archive was not found.", sourceArchive);
            }

            using ZipArchive archive = ZipFile.OpenRead(sourceArchive);
            ReadArchive(
                archive,
                Path.GetFileName(sourceArchive),
                valuesByRegion,
                periodStart,
                periodEnd);
        }

        int expectedLength = checked((int)(slotTicks / Resolution.Ticks));
        var result = new Dictionary<string, OperationalDemandData>(StringComparer.OrdinalIgnoreCase);
        foreach (string region in requestedRegions)
        {
            var values = new double[expectedLength];
            int clampedIntervals = 0;
            for (int index = 0; index < expectedLength; index++)
            {
                DateTimeOffset expectedInstant = periodStart + TimeSpan.FromTicks(Resolution.Ticks * index);
                if (!valuesByRegion[region].TryGetValue(expectedInstant, out DemandValue? demandValue))
                {
                    throw new OperationalDemandDataQualityException(
                        $"Operational demand for {region} is missing interval {expectedInstant:o}.");
                }

                // Operational demand goes negative when rooftop solar exceeds underlying demand
                // (a real, recurring condition in SA1); the model has no concept of negative demand.
                // Clamped here, after the duplicate-conflict check above has compared raw readings,
                // so two genuinely conflicting negative readings cannot be flattened into agreement.
                double megawatts = demandValue.Megawatts;
                if (megawatts < 0)
                {
                    megawatts = 0;
                    clampedIntervals++;
                }

                values[index] = megawatts;
            }

            result.Add(region, new OperationalDemandData(
                region,
                new FlowSeries(periodStart, Resolution, values),
                sourceArchives.Select(path => Path.GetFileName(path)!).ToArray(),
                clampedIntervals));
        }

        return result;
    }

    private static void ReadArchive(
        ZipArchive archive,
        string sourcePath,
        IReadOnlyDictionary<string, Dictionary<DateTimeOffset, DemandValue>> valuesByRegion,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.Length == 0)
            {
                continue;
            }

            string entryPath = $"{sourcePath}!{entry.FullName}";
            if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using Stream entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);
                buffer.Position = 0;
                using var nestedArchive = new ZipArchive(buffer, ZipArchiveMode.Read);
                ReadArchive(
                    nestedArchive,
                    entryPath,
                    valuesByRegion,
                    periodStart,
                    periodEnd);
            }
            else if (entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                using Stream entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                ReadCsv(
                    reader,
                    entryPath,
                    valuesByRegion,
                    periodStart,
                    periodEnd);
            }
        }
    }

    private static void ReadCsv(
        TextReader reader,
        string sourcePath,
        IReadOnlyDictionary<string, Dictionary<DateTimeOffset, DemandValue>> valuesByRegion,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        using var csv = new CsvReader(reader, CsvConfiguration);
        IReadOnlyDictionary<string, int>? columns = null;

        while (csv.Read())
        {
            string[] record = csv.Parser.Record ?? [];
            if (record.Length < 4
                || !record[1].Equals("OPERATIONAL_DEMAND", StringComparison.OrdinalIgnoreCase)
                || !record[2].Equals("ACTUAL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (record[0].Equals("I", StringComparison.OrdinalIgnoreCase))
            {
                columns = record
                    .Select((name, index) => (name, index))
                    .Skip(4)
                    .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
                RequireColumn(columns, "REGIONID", sourcePath);
                RequireColumn(columns, "INTERVAL_DATETIME", sourcePath);
                RequireColumn(columns, "OPERATIONAL_DEMAND", sourcePath);
                continue;
            }

            if (!record[0].Equals("D", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (columns is null)
            {
                throw new FormatException(
                    $"{sourcePath}: operational-demand data appeared before its column definition.");
            }

            string recordRegion = Field(record, columns, "REGIONID", sourcePath);
            if (!valuesByRegion.TryGetValue(recordRegion, out Dictionary<DateTimeOffset, DemandValue>? valuesByIntervalStart))
            {
                continue;
            }

            DateTime intervalEndLocal = DateTime.ParseExact(
                Field(record, columns, "INTERVAL_DATETIME", sourcePath),
                "yyyy/MM/dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);
            DateTimeOffset intervalStart = new DateTimeOffset(intervalEndLocal, NemOffset) - Resolution;
            if (intervalStart < periodStart || intervalStart >= periodEnd)
            {
                continue;
            }

            double megawatts = double.Parse(
                Field(record, columns, "OPERATIONAL_DEMAND", sourcePath),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
            if (!double.IsFinite(megawatts))
            {
                throw new OperationalDemandDataQualityException(
                    $"{sourcePath}: invalid operational demand {megawatts} MW for {recordRegion} at {intervalStart:o}.");
            }

            if (valuesByIntervalStart.TryGetValue(intervalStart, out DemandValue? existing))
            {
                if (existing.Megawatts != megawatts)
                {
                    throw new OperationalDemandDataQualityException(
                        $"Conflicting operational demand for {recordRegion} at {intervalStart:o}: "
                        + $"{existing.Megawatts} MW from {existing.SourcePath} and "
                        + $"{megawatts} MW from {sourcePath}.");
                }

                continue;
            }

            valuesByIntervalStart.Add(intervalStart, new DemandValue(megawatts, sourcePath));
        }
    }

    private static string Field(
        string[] record,
        IReadOnlyDictionary<string, int> columns,
        string name,
        string sourcePath)
    {
        int index = columns[name];
        if (index >= record.Length)
        {
            throw new FormatException($"{sourcePath}: data row is missing column {name}.");
        }

        return record[index];
    }

    private static void RequireColumn(
        IReadOnlyDictionary<string, int> columns,
        string name,
        string sourcePath)
    {
        if (!columns.ContainsKey(name))
        {
            throw new FormatException($"{sourcePath}: column definition is missing {name}.");
        }
    }

    private sealed record DemandValue(double Megawatts, string SourcePath);
}