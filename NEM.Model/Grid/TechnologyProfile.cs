namespace NEM.Model.Grid
{
    public enum GenerationTechnology
    {
        // Note: ENUM values here are akin to ranking of SMRC. This is temporary
        Solar = 1,
        Wind = 2,
        Hydro = 3,
        Coal = 4,
        Gas = 5,
    }

    public enum StorageTechnology { Battery, PumpedHydro }

    public sealed class StorageTechnologyProfile
    {
        public StorageTechnologyProfile(
            double technicalLifeYears,
            double roundTripEfficiency)
        {
            if (technicalLifeYears <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(technicalLifeYears),
                    "Technical life must be positive.");
            }

            if (roundTripEfficiency is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundTripEfficiency),
                    "Round-trip efficiency must be between zero and one.");
            }

            TechnicalLifeYears = technicalLifeYears;
            RoundTripEfficiency = roundTripEfficiency;
        }

        public double TechnicalLifeYears { get; }
        public double RoundTripEfficiency { get; }

        public static StorageTechnologyProfile ProfileFor(StorageTechnology storageTechnology) =>
            storageTechnology switch
            {
                StorageTechnology.Battery => new(
                    technicalLifeYears: 15,
                    roundTripEfficiency: 0.87),
                StorageTechnology.PumpedHydro => new(
                    technicalLifeYears: 50,
                    roundTripEfficiency: 0.78),
                _ => throw new ArgumentOutOfRangeException(nameof(storageTechnology)),
            };
    }

    // TODO: implement Generation Technology Profile when required
}