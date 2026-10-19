using System.Globalization;
using System.Text;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Scenarios;

/// <summary>
/// The one-row-per-point view of a finished sweep: printed at the end of a run, and written beside
/// the index as <c>summary.csv</c> and <c>summary.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The index already carries every one of these values, but it carries them nested per point and
/// per region alongside everything else a point recorded, so reading the shape of a sweep off it
/// means writing a program. The shape of the sweep is the reason the sweep was run.
/// </para>
/// <para>
/// Deliberately a projection and not a new source of truth: every column here is copied from the
/// index entry, so a summary that disagreed with the index would be a bug in this file rather than
/// a second opinion.
/// </para>
/// </remarks>
internal static class SweepSummary
{
    /// <summary>Columns of <c>summary.csv</c>, in order. The header the file is read by.</summary>
    private static readonly string[] CsvHeader =
    [
        "pointId",
        "label",
        "axisValue",
        "status",
        "reliabilityTargetMet",
        "unservedEnergyPercentageOfDemand",
        "totalBatteryEnergyMwh",
        "slcoeAudPerMwh",
    ];

    /// <summary>Writes the sweep's summary artifacts into its published directory.</summary>
    /// <param name="sweepDirectory">The sweep's own output directory.</param>
    /// <param name="definition">The definition that was run, for its axis caption.</param>
    /// <param name="points">Every point's index entry, in axis order.</param>
    public static void Write(
        string sweepDirectory,
        SweepDefinition definition,
        IReadOnlyList<SweepIndexPointDTO> points)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(points);
        SweepSummaryRowDTO[] rows = [.. points.Select(Row)];
        JsonFile.Write(
            new SweepSummaryDTO(
                ArtifactSchemaVersions.SweepSummary,
                definition.SweepId,
                definition.Name,
                new SweepAxisDTO(definition.Axis.Label, definition.Axis.Unit),
                rows),
            Path.Combine(sweepDirectory, "summary.json"));
        File.WriteAllText(
            Path.Combine(sweepDirectory, "summary.csv"),
            BuildCsv(rows),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Prints the same rows as a fixed-width table.</summary>
    /// <param name="output">Where to print.</param>
    /// <param name="definition">The definition that was run, for its axis caption.</param>
    /// <param name="points">Every point's index entry, in axis order.</param>
    public static void WriteTable(
        TextWriter output,
        SweepDefinition definition,
        IReadOnlyList<SweepIndexPointDTO> points)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return;
        }

        string axisHeader = $"{definition.Axis.Label} ({definition.Axis.Unit})";
        string[] headers = ["point", axisHeader, "target", "unmet %", "battery MWh", "SLCoE"];
        List<string[]> table = [headers];
        foreach (SweepIndexPointDTO point in points)
        {
            SweepSummaryRowDTO row = Row(point);
            table.Add([
                row.PointId,
                Format(row.AxisValue),
                row.Status == SweepPointStatus.Succeeded
                    ? row.ReliabilityTargetMet switch { true => "met", false => "NOT met", _ => "-" }
                    : "failed",
                Format(row.UnservedEnergyPercentageOfDemand, "F4"),
                Format(row.TotalBatteryEnergyMwh, "N1"),
                Format(row.SlcoeAudPerMwh, "F2"),
            ]);
        }

        int[] widths = Enumerable.Range(0, headers.Length)
            .Select(column => table.Max(row => row[column].Length))
            .ToArray();
        output.WriteLine();
        foreach (string[] row in table)
        {
            // Right-aligned past the first column, because every one of them is a number and a
            // ragged column of figures is what makes a table unreadable.
            output.WriteLine(string.Join(
                "  ",
                row.Select((cell, column) => column == 0
                    ? cell.PadRight(widths[column])
                    : cell.PadLeft(widths[column]))).TrimEnd());
        }

        output.WriteLine();
    }

    /// <summary>Projects one index entry onto the summary's columns.</summary>
    private static SweepSummaryRowDTO Row(SweepIndexPointDTO point) =>
        new(
            point.PointId,
            point.Label,
            point.AxisValue,
            point.Status,
            point.Reliability?.WithinTarget,
            point.Scalars?.UnservedEnergyPercentageOfDemand,
            // The battery-only figure, not scalars.StorageEnergyMwh, which also counts storage the
            // search cannot grow. A summary column headed "battery" has to be the battery.
            point.Scalars?.TotalBatteryEnergyMwh,
            point.Scalars?.SlcoeAudPerMwh);

    private static string BuildCsv(IReadOnlyList<SweepSummaryRowDTO> rows)
    {
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", CsvHeader));
        foreach (SweepSummaryRowDTO row in rows)
        {
            csv.AppendLine(string.Join(",", (string[])
            [
                Quote(row.PointId),
                Quote(row.Label),
                Format(row.AxisValue),
                Quote(StatusName(row.Status)),
                row.ReliabilityTargetMet switch { true => "true", false => "false", _ => "" },
                Format(row.UnservedEnergyPercentageOfDemand),
                Format(row.TotalBatteryEnergyMwh),
                Format(row.SlcoeAudPerMwh),
            ]));
        }

        return csv.ToString();
    }

    /// <summary>
    /// A missing value is an empty field rather than a zero. A failed point has no SLCoE, and
    /// printing <c>0</c> for it would put a number in a chart where there is no measurement.
    /// </summary>
    private static string Format(double? value, string format = "G17") =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Format(decimal? value, string format = "G") =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// The status as the JSON artifacts spell it, so a reader joining the CSV to <c>summary.json</c>
    /// or to a point's status file matches on the same token.
    /// </summary>
    private static string StatusName(SweepPointStatus status) => status switch
    {
        SweepPointStatus.Succeeded => "succeeded",
        SweepPointStatus.Failed => "failed",
        _ => status.ToString(),
    };

    /// <summary>Quotes a field by the usual CSV rule, because a label is free text.</summary>
    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
