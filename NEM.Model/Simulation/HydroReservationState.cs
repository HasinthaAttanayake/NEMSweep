using NEM.Model.Units;

namespace NEM.Model.Simulation;

/// <summary>
/// Causal pacer for a monthly generation budget (conventional Hydro's reservoir, in
/// practice). Observes residual demand one interval at a time - never anything beyond the
/// interval currently being dispatched - and computes a per-interval offtake cap so the
/// fleet's output tracks its remaining budget over the intervals left in the month, instead
/// of the budget being spent on whichever hours happen to come first each month.
///
/// The core idea is a threshold T: dispatch is capped at
/// <c>min(nameplate, max(0, residualDemand - T))</c>, i.e. the fleet only runs above a
/// residual-demand floor. T is solved each interval by bisection so that, applied
/// retrospectively over a trailing window of past residual-demand observations, it would
/// have spent exactly the budget affordable per interval going forward
/// (<c>remaining / intervalsLeft</c>). A gate that is too low exhausts the budget before the
/// month's real peaks; a threshold-based cap self-calibrates to whatever the demand
/// distribution actually looks like, without per-region tuning.
///
/// No foresight: every input is either the scenario-declared budget (known up front, not a
/// forecast), calendar arithmetic, the current interval's own residual demand, or a window of
/// strictly PAST observations pushed in via <see cref="Observe"/>. Nothing here ever reads
/// ahead of the interval it is asked to price. NEM-076.
/// </summary>
internal sealed class HydroReservationState
{
    /// <summary>
    /// Share of the monthly budget reserved for <see cref="RegionalDispatchRun.DispatchHydroFallback"/>
    /// rather than paced through this controller. The 90% majority is metered against demand
    /// as it's observed; the 10% reserve is a true last-resort backstop, spent only when
    /// storage still leaves a local deficit after everything else (including the paced 90%)
    /// has run. See <see cref="GenerationBudgetState"/>.
    /// </summary>
    internal const double ReserveFraction = 0.10;

    private const int WindowSize = 336;
    private const int WarmUpIntervals = 48;
    private const int BisectionIterations = 50;

    private readonly double[] _ring = new double[WindowSize];
    private readonly List<double> _sorted = new(WindowSize);
    private int _count;
    private int _next;

    /// <summary>
    /// Records this interval's residual demand for use by future intervals' thresholds. Call
    /// once per interval, after the interval's own dispatch decision has already been made -
    /// this is what keeps a later interval's pacing from ever influencing an earlier one.
    /// </summary>
    internal void Observe(Power residualDemand)
    {
        double value = Math.Max(0, residualDemand.Megawatts);
        if (_count == WindowSize)
        {
            RemoveSorted(_ring[_next]);
        }
        else
        {
            _count++;
        }

        _ring[_next] = value;
        _next = (_next + 1) % WindowSize;
        InsertSorted(value);
    }

    /// <summary>
    /// The most this fleet should dispatch this interval: <c>min(nameplate, remainingBudget,
    /// max(0, residualDemand - T))</c> for a threshold T calibrated against the trailing
    /// window so average offtake matches what the remaining budget can afford over the
    /// intervals left in the month. During warm-up (fewer than 48 observations - effectively
    /// only the first two days of a run), there is no usable history to calibrate a threshold
    /// against, so the fleet instead runs flat at the affordable average
    /// (<c>remaining / intervalsLeft</c>), which costs roughly 0.5% of the annual budget.
    /// </summary>
    internal Power OfftakeCap(
        Power nameplateCapacity,
        Energy remainingBudget,
        int intervalsLeftInMonth,
        Power residualDemand,
        TimeSpan resolution)
    {
        if (remainingBudget <= Energy.Zero || intervalsLeftInMonth <= 0)
        {
            return Power.Zero;
        }

        Power residual = Power.Max(Power.Zero, residualDemand);
        Power pace = remainingBudget / (resolution * intervalsLeftInMonth);

        if (_count < WarmUpIntervals)
        {
            return Power.Min(Power.Min(nameplateCapacity, residual), pace);
        }

        double capMw = nameplateCapacity.Megawatts;
        double paceMw = pace.Megawatts;
        double[] prefix = BuildPrefixSums();

        if (MeanOfftakeAt(prefix, capMw, 0) <= paceMw)
        {
            // Even running flat-out whenever there was any residual demand, this fleet's
            // recent history wouldn't have used the whole affordable pace - no gating needed.
            return Power.Min(nameplateCapacity, residual);
        }

        double lowerT = 0;
        double upperT = _sorted[^1];
        for (int iteration = 0; iteration < BisectionIterations; iteration++)
        {
            double midT = (lowerT + upperT) / 2;
            if (MeanOfftakeAt(prefix, capMw, midT) > paceMw)
            {
                lowerT = midT;
            }
            else
            {
                upperT = midT;
            }
        }

        double threshold = (lowerT + upperT) / 2;
        return Power.FromMegawatts(Math.Min(capMw, Math.Max(0, residual.Megawatts - threshold)));
    }

    /// <summary>
    /// Mean offtake per window interval at threshold <paramref name="thresholdMw"/>:
    /// mean(min(capMw, max(0, r - T))). Uses the sorted window and its prefix-sum array so
    /// each evaluation is O(log n) - cheap enough for <see cref="BisectionIterations"/> calls
    /// per interval.
    /// </summary>
    private double MeanOfftakeAt(double[] prefix, double capMw, double thresholdMw)
    {
        int n = _sorted.Count;
        int lowIndex = UpperBound(thresholdMw);
        int highIndex = UpperBound(thresholdMw + capMw);
        double sumInBand = prefix[highIndex] - prefix[lowIndex];
        double offtake = (sumInBand - (thresholdMw * (highIndex - lowIndex)))
            + (capMw * (n - highIndex));
        return offtake / n;
    }

    /// <summary>Count of window elements &lt;= <paramref name="value"/>.</summary>
    private int UpperBound(double value)
    {
        int low = 0;
        int high = _sorted.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (_sorted[mid] <= value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private double[] BuildPrefixSums()
    {
        var prefix = new double[_sorted.Count + 1];
        for (int index = 0; index < _sorted.Count; index++)
        {
            prefix[index + 1] = prefix[index] + _sorted[index];
        }

        return prefix;
    }

    private void InsertSorted(double value)
    {
        int index = _sorted.BinarySearch(value);
        _sorted.Insert(index >= 0 ? index : ~index, value);
    }

    /// <summary>
    /// Drops one occurrence of a value falling out of the ring buffer. Every value removed
    /// here was inserted by <see cref="InsertSorted"/>, so a miss means the sorted view and
    /// the ring have diverged - fail loudly rather than deleting whatever sits at the
    /// insertion point, which would corrupt the window silently and skew every later
    /// threshold.
    /// </summary>
    private void RemoveSorted(double value)
    {
        int index = _sorted.BinarySearch(value);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Residual-demand window is inconsistent: {value} was not found on eviction.");
        }

        _sorted.RemoveAt(index);
    }
}
