using NEMSweep.Model.Units;

namespace NEMSweep.Model.Simulation;

/// <summary>
/// Reliability measures calculated from the demand and unserved-demand series of a
/// <see cref="DispatchOutcome"/>.
/// </summary>
public sealed record ReliabilityMetrics
{
    private ReliabilityMetrics(
        Energy unservedEnergy,
        Power peakUnservedPower,
        double unservedEnergyPercentageOfDemand,
        int unservedHours,
        double hoursServedFraction)
    {
        UnservedEnergy = unservedEnergy;
        PeakUnservedPower = peakUnservedPower;
        UnservedEnergyPercentageOfDemand = unservedEnergyPercentageOfDemand;
        UnservedHours = unservedHours;
        HoursServedFraction = hoursServedFraction;
    }

    /// <summary>Total demand energy that was not served.</summary>
    public Energy UnservedEnergy { get; }
    /// <summary>Largest hourly unserved-demand power.</summary>
    public Power PeakUnservedPower { get; }
    /// <summary>Unserved energy as a percentage of total demand energy.</summary>
    public double UnservedEnergyPercentageOfDemand { get; }
    /// <summary>Number of intervals in which any demand was unserved.</summary>
    public int UnservedHours { get; }
    /// <summary>Fraction of intervals in which all demand was served.</summary>
    public double HoursServedFraction { get; }

    /// <summary>Calculates reliability measures for a regional dispatch outcome.</summary>
    public static ReliabilityMetrics FromOutcome(DispatchOutcome dispatchOutcome)
    {
        ArgumentNullException.ThrowIfNull(dispatchOutcome);

        Energy unservedEnergy = dispatchOutcome.Unserved.Integrate();
        Energy totalDemand = dispatchOutcome.Demand.Integrate();
        int unservedHours = 0;
        Power peakUnservedPower = Power.Zero;
        for (int index = 0; index < dispatchOutcome.Unserved.Length; index++)
        {
            Power unservedPower = dispatchOutcome.Unserved[index];
            peakUnservedPower = Power.Max(peakUnservedPower, unservedPower);
            if (unservedPower > Power.Zero)
            {
                unservedHours++;
            }
        }

        return new ReliabilityMetrics(
            unservedEnergy,
            peakUnservedPower,
            totalDemand == Energy.Zero
                ? 0
                : 100 * (unservedEnergy / totalDemand),
            unservedHours,
            1 - ((double)unservedHours / dispatchOutcome.Unserved.Length));
    }
}
