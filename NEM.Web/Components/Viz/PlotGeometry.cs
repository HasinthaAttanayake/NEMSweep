using System.Globalization;
using System.Text;

namespace NEM.Web.Components.Viz;

/// <summary>
/// Pixel bounds of a plot's data area inside its SVG viewBox. Every plot draws into a fixed
/// 1000-unit-wide viewBox and is scaled by CSS, so one set of bounds serves every screen width.
/// </summary>
public readonly record struct PlotBox(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;
}

/// <summary>
/// A linear axis whose ticks land on values a reader would choose: 1, 2, 2.5 or 5 times a power of
/// ten. The model's series span cents per MWh and tens of millions of MWh, so the tick step is
/// derived from the data rather than fixed, and the label format follows the step's precision.
/// </summary>
public sealed record PlotAxis(double Minimum, double Maximum, IReadOnlyList<double> Ticks, double Step)
{
    private static readonly double[] RoundedSteps = [1, 2, 2.5, 5, 10];

    /// <summary>
    /// An axis covering <paramref name="minimum"/> to <paramref name="maximum"/>. Anchoring at zero
    /// is the default because a cost series moving 3% looks like a collapse on a zoomed axis; a
    /// caller comparing values that never approach zero passes <c>includeZero: false</c> and says so
    /// on the chart.
    /// </summary>
    public static PlotAxis Nice(
        double minimum,
        double maximum,
        int targetTicks = 5,
        bool includeZero = true)
    {
        if (double.IsNaN(minimum) || double.IsNaN(maximum)
            || double.IsInfinity(minimum) || double.IsInfinity(maximum))
        {
            return Fallback();
        }

        if (includeZero)
        {
            minimum = Math.Min(0, minimum);
            maximum = Math.Max(0, maximum);
        }

        if (maximum <= minimum)
        {
            // A flat series still needs an axis with height, or every point lands on one pixel row.
            double padding = Math.Abs(maximum) > 0 ? Math.Abs(maximum) * 0.1 : 1;
            minimum -= padding;
            maximum += padding;
        }

        double step = RoundedStep((maximum - minimum) / Math.Max(1, targetTicks));
        if (step <= 0 || double.IsInfinity(step))
        {
            return Fallback();
        }

        double niceMinimum = Math.Floor(minimum / step) * step;
        double niceMaximum = Math.Ceiling(maximum / step) * step;
        var ticks = new List<double>();
        // Counted rather than accumulated so floating-point drift cannot shift a tick label.
        int tickCount = (int)Math.Round((niceMaximum - niceMinimum) / step);
        for (int index = 0; index <= Math.Min(tickCount, 24); index++)
        {
            ticks.Add(niceMinimum + (step * index));
        }

        return new PlotAxis(niceMinimum, ticks.Count == 0 ? niceMaximum : ticks[^1], ticks, step);
    }

    /// <summary>Where <paramref name="value"/> sits on the axis, 0 at the minimum and 1 at the maximum.</summary>
    public double Fraction(double value) =>
        Maximum <= Minimum ? 0 : (value - Minimum) / (Maximum - Minimum);

    /// <summary>Decimal places the tick step needs, so an axis of 0.25 steps is not labelled "0".</summary>
    public int TickDecimals
    {
        get
        {
            if (Step <= 0 || Step >= 1)
            {
                return 0;
            }

            return Math.Min(6, (int)Math.Ceiling(-Math.Log10(Step)));
        }
    }

    private static double RoundedStep(double raw)
    {
        if (raw <= 0)
        {
            return 0;
        }

        double exponent = Math.Floor(Math.Log10(raw));
        double magnitude = Math.Pow(10, exponent);
        double normalised = raw / magnitude;
        foreach (double candidate in RoundedSteps)
        {
            if (normalised <= candidate)
            {
                return candidate * magnitude;
            }
        }

        return 10 * magnitude;
    }

    private static PlotAxis Fallback() => new(0, 1, [0, 0.5, 1], 0.5);
}

/// <summary>
/// Number formatting shared by every plot. Axis labels and inline figures use the same rules so a
/// value read off an axis matches the same value read out of a table.
/// </summary>
public static class PlotFormat
{
    /// <summary>
    /// A magnitude-scaled figure: 128,000,000 reads as 128M rather than as fourteen characters of
    /// digits that no axis has room for.
    ///
    /// Abbreviation starts at a hundred thousand rather than ten. Storage capacities in these runs
    /// sit either side of ten thousand megawatt-hours, so the lower threshold printed 5,515 beside
    /// 12.3k in the same comparison — two units for one measure, which is exactly what a reader
    /// scanning a row of regions should never have to reconcile.
    /// </summary>
    public static string Compact(double value, int decimals = 1)
    {
        double magnitude = Math.Abs(value);
        string sign = value < 0 ? "-" : string.Empty;
        return magnitude switch
        {
            >= 1e12 => $"{sign}{(magnitude / 1e12).ToString($"N{decimals}", CultureInfo.CurrentCulture)}T",
            >= 1e9 => $"{sign}{(magnitude / 1e9).ToString($"N{decimals}", CultureInfo.CurrentCulture)}B",
            >= 1e6 => $"{sign}{(magnitude / 1e6).ToString($"N{decimals}", CultureInfo.CurrentCulture)}M",
            >= 1e5 => $"{sign}{(magnitude / 1e3).ToString($"N{decimals}", CultureInfo.CurrentCulture)}k",
            >= 1 => value.ToString("N0", CultureInfo.CurrentCulture),
            0 => "0",
            _ => value.ToString("G3", CultureInfo.CurrentCulture),
        };
    }

    /// <summary>Money, with the minus sign outside the symbol so a credit reads as -$5.00.</summary>
    public static string Money(decimal value, int decimals = 2) =>
        (value < 0 ? "-$" : "$") + Math.Abs(value).ToString($"N{decimals}", CultureInfo.CurrentCulture);

    /// <summary>An abbreviated money total: $17,629,045,241 reads as $17.63b.</summary>
    public static string MoneyTotal(decimal value)
    {
        decimal magnitude = Math.Abs(value);
        string sign = value < 0 ? "-" : string.Empty;
        return magnitude switch
        {
            >= 1_000_000_000m => $"{sign}${magnitude / 1_000_000_000m:N2}b",
            >= 1_000_000m => $"{sign}${magnitude / 1_000_000m:N2}m",
            >= 1_000m => $"{sign}${magnitude / 1_000m:N2}k",
            _ => $"{sign}${magnitude:N2}",
        };
    }

    /// <summary>A fraction as a percentage. Shares in these artifacts arrive as 0-1, not 0-100.</summary>
    public static string Share(double fraction, int decimals = 1) =>
        (100 * fraction).ToString($"N{decimals}", CultureInfo.CurrentCulture) + "%";

    /// <summary>
    /// A signed change, for text that states a movement rather than a level. The sign is always
    /// shown because "up 4%" and "down 4%" are different findings.
    /// </summary>
    public static string Signed(double value, string format = "N1") =>
        (value > 0 ? "+" : string.Empty) + value.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>An axis tick label at the precision the axis step needs.</summary>
    public static string Tick(double value, PlotAxis axis, string? prefix = null, bool compact = true)
    {
        string text = compact && Math.Abs(value) >= 10_000
            ? Compact(value, 0)
            : value.ToString($"N{axis.TickDecimals}", CultureInfo.CurrentCulture);
        if (string.IsNullOrEmpty(prefix))
        {
            return text;
        }

        return value < 0 ? $"-{prefix}{text.TrimStart('-')}" : $"{prefix}{text}";
    }

    /// <summary>An SVG coordinate pair, always invariant so a comma decimal separator cannot break a path.</summary>
    public static string Coordinate(double x, double y) =>
        FormattableString.Invariant($"{x:F2},{y:F2}");

    /// <summary>
    /// Appends one coordinate pair to a points attribute under construction, separating it from any
    /// already there. A full-year dispatch view writes several thousand of these per redraw, and
    /// formatting each through an interpolated string allocated two objects per point; writing the
    /// digits straight into the builder allocates none.
    /// </summary>
    public static void AppendCoordinate(StringBuilder builder, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        AppendFixed(builder, x);
        builder.Append(',');
        AppendFixed(builder, y);
    }

    private static void AppendFixed(StringBuilder builder, double value)
    {
        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out int written, "F2", CultureInfo.InvariantCulture))
        {
            builder.Append(buffer[..written]);
            return;
        }

        builder.Append(value.ToString("F2", CultureInfo.InvariantCulture));
    }

    /// <summary>An SVG length, always invariant.</summary>
    public static string Length(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
}

/// <summary>
/// One drawn series. A null value is a genuine gap in the evidence rather than a zero, so the
/// polyline is broken at that position instead of dropping to the floor.
/// </summary>
public sealed record PlotSeries(
    string Name,
    string Color,
    IReadOnlyList<double?> Values,
    bool Dashed = false,
    bool Filled = false,
    bool UseSecondaryAxis = false)
{
    public static PlotSeries From(
        string name,
        string color,
        IEnumerable<double> values,
        bool dashed = false,
        bool filled = false,
        bool useSecondaryAxis = false) =>
        new(name, color, [.. values.Select(value => (double?)value)], dashed, filled, useSecondaryAxis);
}

/// <summary>
/// A vertical rule calling out one position on a plot — the run where a cost curve turns, or the
/// last run before a constraint binds. The model finds these; the chart only draws them.
/// </summary>
public sealed record PlotAnnotation(int Index, string Label);

/// <summary>One plotted run in a trade-off scatter, carrying enough to label and open it.</summary>
public sealed record PlotMarker(
    string Label,
    double X,
    double Y,
    string? Href = null,
    bool IsHighlighted = false,
    string? Detail = null);

/// <summary>One part of a composition, sized by <see cref="Value"/> relative to its siblings.</summary>
public sealed record MixSegment(string Name, string Color, double Value);

/// <summary>
/// One row of a cross-category comparison. <see cref="Display"/> is passed rather than derived so
/// the caller can state money, energy and shares in their own units without the bar knowing about
/// any of them.
/// </summary>
public sealed record CompareRow(
    string Label,
    double Value,
    string Display,
    string? Color = null,
    bool IsMuted = false);

/// <summary>
/// Builds the SVG path data for a series drawn against an axis. Kept out of the components so the
/// geometry can be tested without rendering, and so line, area and marker placement cannot drift
/// apart.
/// </summary>
public static class PlotPath
{
    /// <summary>
    /// Polyline segments for a series, split wherever the series has no value. Each returned string
    /// is the <c>points</c> attribute of one polyline.
    /// </summary>
    public static IReadOnlyList<string> Segments(
        IReadOnlyList<double?> values,
        PlotBox box,
        PlotAxis axis)
    {
        ArgumentNullException.ThrowIfNull(values);

        var segments = new List<string>();
        var current = new StringBuilder();
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] is not { } value)
            {
                Flush(segments, current);
                continue;
            }

            PlotFormat.AppendCoordinate(current, X(index, values.Count, box), Y(value, box, axis));
        }

        Flush(segments, current);
        return segments;
    }

    /// <summary>
    /// A closed polygon between two stacked boundaries, for a filled band. Both boundaries must
    /// have the same length; the upper is drawn left to right and the lower right to left.
    /// </summary>
    public static string Band(
        IReadOnlyList<double> lower,
        IReadOnlyList<double> upper,
        PlotBox box,
        PlotAxis axis)
    {
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(upper);

        int count = Math.Min(lower.Count, upper.Count);
        var points = new StringBuilder(count * 16);
        for (int index = 0; index < count; index++)
        {
            PlotFormat.AppendCoordinate(points, X(index, count, box), Y(upper[index], box, axis));
        }

        for (int index = count - 1; index >= 0; index--)
        {
            PlotFormat.AppendCoordinate(points, X(index, count, box), Y(lower[index], box, axis));
        }

        return points.ToString();
    }

    /// <summary>The horizontal position of the <paramref name="index"/>th of <paramref name="count"/> evenly spaced points.</summary>
    public static double X(int index, int count, PlotBox box) => count <= 1
        ? box.Left + (box.Width / 2)
        : box.Left + (box.Width * index / (count - 1));

    /// <summary>The vertical position of a value on an axis, clamped to the plot area.</summary>
    public static double Y(double value, PlotBox box, PlotAxis axis) =>
        box.Bottom - (box.Height * Math.Clamp(axis.Fraction(value), 0, 1));

    /// <summary>The horizontal position of a value on an axis, for scatter plots with a measured x.</summary>
    public static double XValue(double value, PlotBox box, PlotAxis axis) =>
        box.Left + (box.Width * Math.Clamp(axis.Fraction(value), 0, 1));

    private static void Flush(List<string> segments, StringBuilder current)
    {
        if (current.Length > 0)
        {
            segments.Add(current.ToString());
            current.Clear();
        }
    }
}
