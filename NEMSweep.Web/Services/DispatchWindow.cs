namespace NEMSweep.Web.Services;

/// <summary>How a bucket of intervals collapses into the single value drawn for it.</summary>
public enum IntervalReduction
{
    /// <summary>The mean of the bucket, for a series read as a level.</summary>
    Average,

    /// <summary>The largest value in the bucket, so a three-hour event survives a year view.</summary>
    Peak,

    /// <summary>The value at the start of the bucket, for a state such as stored energy.</summary>
    First,
}

/// <summary>
/// The intervals a view covers and how they map onto drawn points. A dispatch period is 8,760
/// hourly intervals and a chart has a few hundred pixels of width, so every series on a page has to
/// be reduced the same way against the same buckets — otherwise two traces on one pair of axes
/// would not line up.
///
/// Selection and bucketing live here rather than in a page because the same window now drives the
/// single-region charts and one small multiple per region, and the two must agree exactly.
/// </summary>
public sealed class DispatchWindow
{
    private DispatchWindow(
        int[] indexes,
        int bucketSize,
        DateTimeOffset[] timestamps,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        Indexes = indexes;
        BucketSize = bucketSize;
        Timestamps = timestamps;
        Start = start;
        End = end;
    }

    /// <summary>Indices into the full series, in order, that this view covers.</summary>
    public int[] Indexes { get; }

    /// <summary>How many intervals collapse into one drawn point.</summary>
    public int BucketSize { get; }

    /// <summary>The instant at the start of each drawn point.</summary>
    public DateTimeOffset[] Timestamps { get; }

    public DateTimeOffset Start { get; }

    /// <summary>Exclusive: the instant the last selected interval ends.</summary>
    public DateTimeOffset End { get; }

    public int IntervalCount => Indexes.Length;

    public int PointCount => Timestamps.Length;

    public bool IsEmpty => Indexes.Length == 0;

    /// <summary>
    /// Builds the window for a selection over a regular series. <paramref name="targetPoints"/> caps
    /// how many points are drawn; a day view passes the interval count so every hour is kept.
    /// </summary>
    public static DispatchWindow Create(
        DateTimeOffset periodStart,
        TimeSpan resolution,
        int length,
        Func<DateTimeOffset, bool> isSelected,
        int targetPoints)
    {
        ArgumentNullException.ThrowIfNull(isSelected);

        var selected = new List<int>(length);
        for (int index = 0; index < length; index++)
        {
            if (isSelected(periodStart.AddTicks(resolution.Ticks * index)))
            {
                selected.Add(index);
            }
        }

        int[] indexes = [.. selected];
        if (indexes.Length == 0 || resolution <= TimeSpan.Zero)
        {
            return new DispatchWindow([], 1, [], periodStart, periodStart);
        }

        int bucketSize = Math.Max(1, (int)Math.Ceiling((double)indexes.Length / Math.Max(1, targetPoints)));
        int bucketCount = (indexes.Length + bucketSize - 1) / bucketSize;
        var timestamps = new DateTimeOffset[bucketCount];
        for (int bucket = 0; bucket < bucketCount; bucket++)
        {
            timestamps[bucket] = periodStart.AddTicks(resolution.Ticks * indexes[bucket * bucketSize]);
        }

        return new DispatchWindow(
            indexes,
            bucketSize,
            timestamps,
            periodStart.AddTicks(resolution.Ticks * indexes[0]),
            periodStart.AddTicks(resolution.Ticks * indexes[^1]) + resolution);
    }

    /// <summary>
    /// Reduces a full-length series to one value per drawn point. Written as a loop over the index
    /// array: slicing a sub-array per bucket allocated hundreds of arrays per series per redraw.
    /// </summary>
    public double[] Reduce(double[]? series, IntervalReduction reduction)
    {
        double[] values = new double[PointCount];
        if (series is null || series.Length == 0)
        {
            return values;
        }

        for (int bucket = 0; bucket < values.Length; bucket++)
        {
            int start = bucket * BucketSize;
            int end = Math.Min(start + BucketSize, Indexes.Length);
            if (reduction == IntervalReduction.First)
            {
                values[bucket] = At(series, Indexes[start]);
                continue;
            }

            double accumulated = reduction == IntervalReduction.Peak ? double.NegativeInfinity : 0;
            for (int offset = start; offset < end; offset++)
            {
                double value = At(series, Indexes[offset]);
                accumulated = reduction == IntervalReduction.Peak
                    ? Math.Max(accumulated, value)
                    : accumulated + value;
            }

            values[bucket] = reduction == IntervalReduction.Peak
                ? accumulated
                : accumulated / (end - start);
        }

        return values;
    }

    public double[] Average(double[]? series) => Reduce(series, IntervalReduction.Average);

    public double[] Peak(double[]? series) => Reduce(series, IntervalReduction.Peak);

    public double[] First(double[]? series) => Reduce(series, IntervalReduction.First);

    /// <summary>The largest value the series reaches anywhere in the window, before bucketing.</summary>
    public double Maximum(double[]? series)
    {
        if (series is null || series.Length == 0 || IsEmpty)
        {
            return 0;
        }

        double maximum = double.NegativeInfinity;
        foreach (int index in Indexes)
        {
            maximum = Math.Max(maximum, At(series, index));
        }

        return double.IsNegativeInfinity(maximum) ? 0 : maximum;
    }

    /// <summary>The sum over the window, in the series' own units multiplied by interval length.</summary>
    public double Integrate(double[]? series, TimeSpan resolution)
    {
        if (series is null || series.Length == 0)
        {
            return 0;
        }

        double total = 0;
        foreach (int index in Indexes)
        {
            total += At(series, index);
        }

        return total * resolution.TotalHours;
    }

    /// <summary>
    /// A shorter series than the window was built for is read as zero past its end rather than
    /// throwing, so a region whose artifact carries fewer intervals cannot break a comparison.
    /// </summary>
    private static double At(double[] series, int index) =>
        index >= 0 && index < series.Length ? series[index] : 0;
}
