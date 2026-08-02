using NEM.Model.Units;

namespace NEM.Model.Simulation
{
    public sealed record ReliabilityMetrics
    {
        public Energy UnservedEnergy { get; init; }
        public Power PeakUnservedPower { get; init; }
        public double UnservedEnergyPercentageOfDemand { get; init; }
        public int UnservedHours { get; init; }
        public double HoursServedFraction { get; init; }

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

            return new ReliabilityMetrics
            {
                UnservedEnergy = unservedEnergy,
                PeakUnservedPower = peakUnservedPower,
                UnservedEnergyPercentageOfDemand = totalDemand == Energy.Zero
                    ? 0
                    : 100 * (unservedEnergy / totalDemand),
                UnservedHours = unservedHours,
                HoursServedFraction = 1 - ((double)unservedHours / dispatchOutcome.Unserved.Length),
            };
        }
    }
}