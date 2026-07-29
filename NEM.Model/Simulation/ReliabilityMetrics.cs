using NEM.Model.Units;

namespace NEM.Model.Simulation
{
    public sealed record ReliabilityMetrics
    {
        public Energy UnservedEnergy { get; init; }
        public double UnservedEnergyPercentageOfDemand { get; init; }
        public int UnservedHours { get; init; }
        public double HoursServedFraction { get; init; }

        public static ReliabilityMetrics FromOutcome(DispatchOutcome dispatchOutcome)
        {
            ArgumentNullException.ThrowIfNull(dispatchOutcome);

            Energy unservedEnergy = dispatchOutcome.Unserved.Integrate();
            Energy totalDemand = dispatchOutcome.Demand.Integrate();
            int unservedHours = 0;
            for (int index = 0; index < dispatchOutcome.Unserved.Length; index++)
            {
                if (dispatchOutcome.Unserved[index].Megawatts > 0)
                {
                    unservedHours++;
                }
            }

            return new ReliabilityMetrics
            {
                UnservedEnergy = unservedEnergy,
                UnservedEnergyPercentageOfDemand = totalDemand == Energy.Zero
                    ? 0
                    : 100 * (unservedEnergy / totalDemand),
                UnservedHours = unservedHours,
                HoursServedFraction = 1 - ((double)unservedHours / dispatchOutcome.Unserved.Length),
            };
        }
    } 
}