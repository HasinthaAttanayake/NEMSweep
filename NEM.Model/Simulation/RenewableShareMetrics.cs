using NEM.Model.Grid;

namespace NEM.Model.Simulation;

/// <summary>Delivered-generation renewable shares for one regional dispatch outcome.</summary>
public sealed record RenewableShareMetrics(
    double GridScaleShare,
    double NativeShare)
{
    /// <summary>Calculates renewable shares from delivered energy by explicitly typed technology.</summary>
    public static RenewableShareMetrics FromDeliveredEnergy(
        IReadOnlyDictionary<GenerationTechnology, double> deliveredEnergyMwh,
        double nativeDemandMwh)
    {
        ArgumentNullException.ThrowIfNull(deliveredEnergyMwh);
        if (!double.IsFinite(nativeDemandMwh) || nativeDemandMwh < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nativeDemandMwh));
        }

        double solar = EnergyFor(deliveredEnergyMwh, GenerationTechnology.Solar);
        double wind = EnergyFor(deliveredEnergyMwh, GenerationTechnology.Wind);
        double hydro = EnergyFor(deliveredEnergyMwh, GenerationTechnology.Hydro);
        double totalDelivered = deliveredEnergyMwh.Values.Sum(NormalizeEnergy);

        return new RenewableShareMetrics(
            Fraction(solar + wind + hydro, totalDelivered),
            Fraction(solar + wind, nativeDemandMwh));
    }

    internal static RenewableShareMetrics FromOutcome(DispatchOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return FromDeliveredEnergy(
            outcome.PerFleetDelivered.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Integrate().MegawattHours),
            outcome.NativeDemand.Integrate().MegawattHours);
    }

    private static double EnergyFor(
        IReadOnlyDictionary<GenerationTechnology, double> deliveredEnergyMwh,
        GenerationTechnology technology)
    {
        return NormalizeEnergy(deliveredEnergyMwh.GetValueOrDefault(technology));
    }

    private static double NormalizeEnergy(double energy)
    {
        if (!double.IsFinite(energy))
        {
            throw new ArgumentException("Delivered energy must be finite.", nameof(energy));
        }

        return Math.Max(0, energy);
    }

    private static double Fraction(double numerator, double denominator) =>
        denominator == 0 ? 0 : Math.Clamp(numerator / denominator, 0, 1);
}