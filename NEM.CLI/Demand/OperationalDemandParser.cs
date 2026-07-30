using CsvHelper;
using CsvHelper.Configuration;
using NEM.Model.Series;
using System.Globalization;
using System.IO.Compression;

namespace NEM.CLI.Demand;

internal sealed record OperationalDemandData(
    string Region,
    FlowSeries Demand,
    IReadOnlyList<string> SourceArchives);

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

    public static OperationalDemandData ReadFinancialYear(
        string archiveDirectory,
        string region,
        DateTimeOffset periodStart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        if (periodStart.Offset != NemOffset)
        {
            throw new ArgumentException(
                "The operational-demand period start must use NEM market time (UTC+10).",
                nameof(periodStart));
        }

        if (!Directory.Exists(archiveDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Operational-demand archive directory was not found: {archiveDirectory}");
        }

        DateTimeOffset periodEnd = periodStart.AddYears(1);
        var valuesByIntervalStart = new Dictionary<DateTimeOffset, DemandValue>();
        string[] sourceArchives = Directory
            .EnumerateFiles(archiveDirectory, "*.zip", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sourceArchives.Length == 0)
        {
            throw new OperationalDemandDataQualityException(
                $"No ZIP archives were found in {archiveDirectory}.");
        }

        foreach (string sourceArchive in sourceArchives)
        {
            using ZipArchive archive = ZipFile.OpenRead(sourceArchive);
            ReadArchive(
                archive,
                Path.GetFileName(sourceArchive),
                region,
                periodStart,
                periodEnd,
                valuesByIntervalStart);
        }

        int expectedLength = checked((int)((periodEnd - periodStart).Ticks / Resolution.Ticks));
        var values = new double[expectedLength];
        for (int index = 0; index < expectedLength; index++)
        {
            DateTimeOffset expectedInstant = periodStart + TimeSpan.FromTicks(Resolution.Ticks * index);
            if (!valuesByIntervalStart.TryGetValue(expectedInstant, out DemandValue? demandValue))
            {
                throw new OperationalDemandDataQualityException(
                    $"Operational demand for {region} is missing interval {expectedInstant:o}.");
            }

            values[index] = demandValue.Megawatts;
        }

        return new OperationalDemandData(
            region,
            new FlowSeries(periodStart, Resolution, values),
            sourceArchives.Select(Path.GetFileName).ToArray()!);
    }

    private static void ReadArchive(
        ZipArchive archive,
        string sourcePath,
        string region,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        Dictionary<DateTimeOffset, DemandValue> valuesByIntervalStart)
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
                    region,
                    periodStart,
                    periodEnd,
                    valuesByIntervalStart);
            }
            else if (entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                using Stream entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                ReadCsv(
                    reader,
                    entryPath,
                    region,
                    periodStart,
                    periodEnd,
                    valuesByIntervalStart);
            }
        }
    }

    private static void ReadCsv(
        TextReader reader,
        string sourcePath,
        string region,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        Dictionary<DateTimeOffset, DemandValue> valuesByIntervalStart)
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
            if (!recordRegion.Equals(region, StringComparison.OrdinalIgnoreCase))
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
            if (!double.IsFinite(megawatts) || megawatts < 0)
            {
                throw new OperationalDemandDataQualityException(
                    $"{sourcePath}: invalid operational demand {megawatts} MW for {region} at {intervalStart:o}.");
            }

            if (valuesByIntervalStart.TryGetValue(intervalStart, out DemandValue? existing))
            {
                if (existing.Megawatts != megawatts)
                {
                    throw new OperationalDemandDataQualityException(
                        $"Conflicting operational demand for {region} at {intervalStart:o}: "
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