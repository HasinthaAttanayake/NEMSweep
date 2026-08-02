using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Simulation;

internal sealed class GenerationBudgetState
{
    private readonly GenerationTechnology _generationTechnology;
    private readonly Dictionary<DateOnly, double>? _remainingMwhByMonth;

    public GenerationBudgetState(GeneratingFleet fleet)
    {
        ArgumentNullException.ThrowIfNull(fleet);

        _generationTechnology = fleet.GenerationTechnology;
        _remainingMwhByMonth = fleet.MonthlyCapacityFactors?.ToDictionary(
            entry => entry.Key,
            entry => fleet.NameplateCapacity.Megawatts
                * DateTime.DaysInMonth(entry.Key.Year, entry.Key.Month)
                * 24
                * entry.Value);
    }

    public Power Headroom(
        Power availableCapacity,
        Power generated,
        DateTimeOffset instant,
        TimeSpan resolution)
    {
        Power capacityHeadroom = Power.Max(Power.Zero, availableCapacity - generated);
        if (_remainingMwhByMonth is null)
        {
            return capacityHeadroom;
        }

        double remainingMwh = RemainingFor(instant);
        return Power.Min(
            capacityHeadroom,
            Energy.FromMegawattHours(remainingMwh) / resolution);
    }

    public Power Take(Power requested, DateTimeOffset instant, TimeSpan resolution)
    {
        if (requested < Power.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested), requested.Megawatts, "Generation cannot be negative.");
        }

        if (_remainingMwhByMonth is null)
        {
            return requested;
        }

        var month = new DateOnly(instant.Year, instant.Month, 1);
        double remainingMwh = RemainingFor(instant);
        Power accepted = Power.Min(
            requested,
            Energy.FromMegawattHours(remainingMwh) / resolution);
        _remainingMwhByMonth[month] = Math.Max(
            0,
            remainingMwh - (accepted * resolution).MegawattHours);
        return accepted;
    }

    private double RemainingFor(DateTimeOffset instant)
    {
        var month = new DateOnly(instant.Year, instant.Month, 1);
        if (_remainingMwhByMonth is null)
        {
            return double.PositiveInfinity;
        }

        if (!_remainingMwhByMonth.TryGetValue(month, out double remainingMwh))
        {
            throw new InvalidOperationException(
                $"{_generationTechnology} has no energy budget for {month:yyyy-MM}.");
        }

        return remainingMwh;
    }
}