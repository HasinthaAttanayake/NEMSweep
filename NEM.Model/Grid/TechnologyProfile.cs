using NEM.Model.Units;

namespace NEM.Model.Grid
{
    public enum GenerationTechnology
    {
        Solar,
        Wind,
        Hydro,
        Coal,
        Gas,
    }

    public enum StorageTechnology { Battery, PumpedHydro }

    /// <summary>
    /// Technical assumptions for one scenario generation fleet.
    /// </summary>
    public sealed record GenerationTechnologyProfile
    {
        public GenerationTechnologyProfile(HeatRate heatRate, uint technicalLifeYears)
        {
            if (technicalLifeYears == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(technicalLifeYears));
            }

            HeatRate = heatRate;
            TechnicalLifeYears = technicalLifeYears;
        }

        /// <summary>Thermal energy required to deliver one MWh of electricity.</summary>
        public HeatRate HeatRate { get; }

        /// <summary>Expected operating life of the generating asset in years.</summary>
        public uint TechnicalLifeYears { get; }
    }

    public sealed record StorageTechnologyProfile
    {
        public StorageTechnologyProfile(
            uint technicalLifeYears,
            double roundTripEfficiency)
        {
            if (technicalLifeYears == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(technicalLifeYears),
                    "Technical life must be positive.");
            }

            if (double.IsNaN(roundTripEfficiency)
                || double.IsInfinity(roundTripEfficiency)
                || roundTripEfficiency is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundTripEfficiency),
                    "Round-trip efficiency must be between zero and one.");
            }

            TechnicalLifeYears = technicalLifeYears;
            RoundTripEfficiency = roundTripEfficiency;
        }

        public uint TechnicalLifeYears { get; }
        public double RoundTripEfficiency { get; }
    }

}