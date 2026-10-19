using System.Globalization;
using System.Text;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;

namespace NEMSweep.CLI.Scenarios;

/// <summary>
/// The point-to-axis-value table a fan-out prints and writes.
/// </summary>
/// <remarks>
/// A point ID encodes nothing. <c>p9</c> says nothing about where that point sits on the axis, and
/// nothing checks that an ID and its axis value agree. Until now the mapping was only in the
/// definition a caller had just handed over and in an index that does not exist until a run
/// finishes, so anyone wanting to run one point by name had to read the definition back. Fan-out is
/// where that question is asked, so fan-out is where the answer belongs.
/// </remarks>
internal static class SweepPointMap
{
    /// <summary>The file a fan-out leaves beside the configs it generated.</summary>
    public const string FileName = "points.csv";

    /// <summary>Prints the table and writes it next to the generated configs.</summary>
    /// <param name="context">The invocation's workspace and output.</param>
    /// <param name="definition">The definition that was fanned out.</param>
    public static void Write(CliContext context, SweepDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(definition);
        string path = Path.Combine(
            SweepFanOutCommand.ConfigOutputDirectory(context, definition),
            FileName);
        File.WriteAllText(
            path,
            BuildCsv(definition),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Print(context.Output, definition);
        context.Output.WriteLine($"Wrote point map: {Path.GetFullPath(path)}");
    }

    /// <summary>Prints the table alone, without writing anything.</summary>
    /// <param name="output">Where to print.</param>
    /// <param name="definition">The definition whose points to tabulate.</param>
    public static void Print(TextWriter output, SweepDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(definition);
        string[] headers = ["point", $"{definition.Axis.Label} ({definition.Axis.Unit})", "label"];
        List<string[]> rows =
        [
            headers,
            .. definition.Points.Select(point => new[]
            {
                point.PointId,
                point.AxisValue.ToString("G17", CultureInfo.InvariantCulture),
                point.Label,
            }),
        ];
        int[] widths = Enumerable.Range(0, headers.Length)
            .Select(column => rows.Max(row => row[column].Length))
            .ToArray();
        output.WriteLine();
        foreach (string[] row in rows)
        {
            // The axis value is right-aligned and the two strings are not, so a column of numbers
            // reads as a column of numbers.
            output.WriteLine(string.Join(
                "  ",
                row.Select((cell, column) => column == 1
                    ? cell.PadLeft(widths[column])
                    : cell.PadRight(widths[column]))).TrimEnd());
        }

        output.WriteLine();
    }

    private static string BuildCsv(SweepDefinition definition)
    {
        var csv = new StringBuilder();
        csv.AppendLine("pointId,axisValue,label,axisLabel,axisUnit");
        foreach (SweepPoint point in definition.Points)
        {
            csv.AppendLine(string.Join(",", (string[])
            [
                Quote(point.PointId),
                point.AxisValue.ToString("G17", CultureInfo.InvariantCulture),
                Quote(point.Label),
                Quote(definition.Axis.Label),
                Quote(definition.Axis.Unit),
            ]));
        }

        return csv.ToString();
    }

    /// <summary>Quotes a field by the usual CSV rule, because a label is free text.</summary>
    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
