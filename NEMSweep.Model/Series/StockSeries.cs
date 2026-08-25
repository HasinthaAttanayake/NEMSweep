using NEMSweep.Model.Units;

namespace NEMSweep.Model.Series;

/// <summary>
/// A stock series in megawatt-hours (MWh): state of charge only. A stock is a
/// level at an instant, not something computed with: it never appears in the
/// energy balance and is a diagnostic output trajectory.
/// <para>
/// It has <b>no combination operators and no resample method, by design</b>. It
/// cannot be summed as a flow because it cannot be summed at all: summing state
/// of charge across intervals yields a number with no physical meaning.
/// </para>
/// <para>
/// A stock resolution change would select or sample a value appropriate to an instant,
/// rather than aggregate values over an interval. This is intentionally unsupported:
/// state of charge is never resampled in the model.
/// </para>
/// </summary>
public sealed class StockSeries : TimeSeries
{
    /// <summary>
    /// Creates a <see cref="StockSeries"/> from a state-of-charge trace in MWh. Every
    /// value must be non-negative, because stocks are unsigned. The upper bound (capacity)
    /// is enforced where the storage fleet is known, not here.
    /// </summary>
    /// <param name="start">Start of the first interval.</param>
    /// <param name="resolution">Interval duration; must be positive.</param>
    /// <param name="megawattHours">State of charge at each interval, in MWh; must be non-negative.</param>
    public StockSeries(DateTimeOffset start, TimeSpan resolution, double[] megawattHours)
        : base(start, resolution, megawattHours)
    {
        for (int i = 0; i < megawattHours.Length; i++)
        {
            if (megawattHours[i] < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(megawattHours),
                    megawattHours[i],
                    $"State of charge cannot be negative (index {i}); stocks are unsigned. " +
                    "The upper bound (≤ capacity) is enforced where the storage fleet is known.");
            }
        }
    }

    /// <summary>
    /// State of charge at <paramref name="index"/> (MWh): the level at the
    /// <b>start</b> of interval <c>index</c> (interval-beginning, per
    /// <see cref="TimeSeries.InstantAt"/>).
    /// </summary>
    public Energy this[int index] => Energy.FromMegawattHours(RawValue(index));
}
