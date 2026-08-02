using NEM.Model.Units;

namespace NEM.Model.Simulation
{
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

        public Energy UnservedEnergy { get; }
        public Power PeakUnservedPower { get; }
        public double UnservedEnergyPercentageOfDemand { get; }
        public int UnservedHours { get; }
        public double HoursServedFraction { get; }

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
}