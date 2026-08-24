using NEMSweep.Model.Grid;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Simulation;

/// <summary>
/// Tracks a fleet's monthly energy budget as two independent pools: a "paced" pool (the
/// share <see cref="HydroReservationState"/> meters against observed demand,
/// <c>1 - HydroReservationState.ReserveFraction</c> of the month's total) and a "reserve" pool
/// (the remaining <see cref="HydroReservationState.ReserveFraction"/>, spent only by
/// <see cref="RegionalDispatchRun.DispatchHydroFallback"/> as a true last resort after storage).
/// A fleet with no monthly budget (every technology except conventional Hydro) has both pools
/// permanently empty and every method here is then a capacity-only pass-through, matching the
/// pre-split behaviour exactly.
/// </summary>
internal sealed class GenerationBudgetState
{
    private readonly GenerationTechnology _generationTechnology;
    private readonly Dictionary<DateOnly, double>? _remainingPacedMwhByMonth;
    private readonly Dictionary<DateOnly, double>? _remainingReserveMwhByMonth;

    public GenerationBudgetState(GeneratingFleet fleet, double reserveFraction = 0)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        if (reserveFraction is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reserveFraction), reserveFraction, "Reserve fraction must be within [0, 1).");
        }

        _generationTechnology = fleet.GenerationTechnology;
        if (fleet.MonthlyCapacityFactors is null)
        {
            return;
        }

        _remainingPacedMwhByMonth = new Dictionary<DateOnly, double>();
        _remainingReserveMwhByMonth = new Dictionary<DateOnly, double>();
        foreach ((DateOnly month, double capacityFactor) in fleet.MonthlyCapacityFactors)
        {
            double totalMwh = fleet.NameplateCapacity.Megawatts
                * DateTime.DaysInMonth(month.Year, month.Month)
                * 24
                * capacityFactor;
            double reserveMwh = totalMwh * reserveFraction;
            _remainingReserveMwhByMonth[month] = reserveMwh;
            _remainingPacedMwhByMonth[month] = totalMwh - reserveMwh;
        }
    }

    /// <summary>Headroom against the paced pool - what <see cref="HydroReservationState"/> meters.</summary>
    public Power Headroom(Power availableCapacity, Power generated, DateTimeOffset instant, TimeSpan resolution) =>
        HeadroomFrom(_remainingPacedMwhByMonth, availableCapacity, generated, instant, resolution);

    /// <summary>Draws from the paced pool.</summary>
    public Power Take(Power requested, DateTimeOffset instant, TimeSpan resolution) =>
        TakeFrom(_remainingPacedMwhByMonth, requested, instant, resolution);

    /// <summary>Remaining MWh in the paced pool for <paramref name="instant"/>'s month.</summary>
    public Energy PacedRemaining(DateTimeOffset instant) =>
        _remainingPacedMwhByMonth is null
            ? Energy.Zero
            : Energy.FromMegawattHours(RemainingFor(_remainingPacedMwhByMonth, instant));

    /// <summary>Headroom against the reserve pool - the final-backstop share only.</summary>
    public Power ReserveHeadroom(Power availableCapacity, Power generated, DateTimeOffset instant, TimeSpan resolution) =>
        HeadroomFrom(_remainingReserveMwhByMonth, availableCapacity, generated, instant, resolution);

    /// <summary>Draws from the reserve pool.</summary>
    public Power TakeReserve(Power requested, DateTimeOffset instant, TimeSpan resolution) =>
        TakeFrom(_remainingReserveMwhByMonth, requested, instant, resolution);

    /// <summary>
    /// Moves whatever is left of this month's reserve pool into its paced pool, and returns
    /// the energy moved.
    ///
    /// The reserve exists to hold cover for a deficit storage cannot meet. That is worth
    /// paying for while the month still has hours in which such a deficit could arise, but
    /// unspent reserve does not carry into the next month (see <see cref="RemainingFor"/> -
    /// each month has its own pool), so near the month's end its option value collapses to
    /// zero: hold it and it is simply lost. Releasing it into the paced pool hands it to
    /// <see cref="HydroReservationState"/>, which spends it on that window's highest
    /// residual-demand hours rather than dumping it. Idempotent - a second call in the same
    /// month moves nothing. NEM-076.
    /// </summary>
    public Energy ReleaseUnspentReserve(DateTimeOffset instant)
    {
        if (_remainingReserveMwhByMonth is null || _remainingPacedMwhByMonth is null)
        {
            return Energy.Zero;
        }

        double reserveMwh = RemainingFor(_remainingReserveMwhByMonth, instant);
        if (reserveMwh <= 0)
        {
            return Energy.Zero;
        }

        var month = new DateOnly(instant.Year, instant.Month, 1);
        _remainingReserveMwhByMonth[month] = 0;
        _remainingPacedMwhByMonth[month] =
            RemainingFor(_remainingPacedMwhByMonth, instant) + reserveMwh;
        return Energy.FromMegawattHours(reserveMwh);
    }

    private Power HeadroomFrom(
        Dictionary<DateOnly, double>? pool,
        Power availableCapacity,
        Power generated,
        DateTimeOffset instant,
        TimeSpan resolution)
    {
        Power capacityHeadroom = Power.Max(Power.Zero, availableCapacity - generated);
        if (pool is null)
        {
            return capacityHeadroom;
        }

        double remainingMwh = RemainingFor(pool, instant);
        return Power.Min(
            capacityHeadroom,
            Energy.FromMegawattHours(remainingMwh) / resolution);
    }

    private Power TakeFrom(
        Dictionary<DateOnly, double>? pool,
        Power requested,
        DateTimeOffset instant,
        TimeSpan resolution)
    {
        if (requested < Power.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested), requested.Megawatts, "Generation cannot be negative.");
        }

        if (pool is null)
        {
            return requested;
        }

        var month = new DateOnly(instant.Year, instant.Month, 1);
        double remainingMwh = RemainingFor(pool, instant);
        Power accepted = Power.Min(
            requested,
            Energy.FromMegawattHours(remainingMwh) / resolution);
        pool[month] = Math.Max(
            0,
            remainingMwh - (accepted * resolution).MegawattHours);
        return accepted;
    }

    private double RemainingFor(Dictionary<DateOnly, double>? pool, DateTimeOffset instant)
    {
        var month = new DateOnly(instant.Year, instant.Month, 1);
        if (pool is null)
        {
            return double.PositiveInfinity;
        }

        if (!pool.TryGetValue(month, out double remainingMwh))
        {
            throw new InvalidOperationException(
                $"{_generationTechnology} has no energy budget for {month:yyyy-MM}.");
        }

        return remainingMwh;
    }
}
