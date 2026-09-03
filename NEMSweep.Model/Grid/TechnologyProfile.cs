using NEMSweep.Model.Units;

namespace NEMSweep.Model.Grid;

/// <summary>
/// The generation technologies the model dispatches. This is the model's core vocabulary: a
/// fleet, a merit-order position, a per-technology flow series and a cost contribution are all
/// keyed by one of these values.
/// </summary>
/// <remarks>
/// The set is closed on purpose. Adding a technology is a modelling decision with consequences
/// for merit order, renewable-share classification and cost aggregation, so it is a code change
/// rather than a configuration one. Dispatch orders fleets by short-run marginal cost and breaks
/// ties by this enum's declaration order, which is what makes a run deterministic.
/// </remarks>
public enum GenerationTechnology
{
    /// <summary>Grid-scale solar. Output follows the region's irradiance trace; counted as renewable.</summary>
    Solar,

    /// <summary>Grid-scale wind. Output follows the region's wind trace; counted as renewable.</summary>
    Wind,

    /// <summary>
    /// Conventional hydro. Dispatchable, but unlike every other technology it is limited by a
    /// monthly energy budget rather than by fuel cost, so it is paced across the month instead
    /// of run whenever it is cheapest. Counted as grid-scale renewable.
    /// </summary>
    Hydro,

    /// <summary>Coal-fired thermal generation. Priced by fuel price multiplied by heat rate.</summary>
    Coal,

    /// <summary>Gas-fired thermal generation. Priced by fuel price multiplied by heat rate.</summary>
    Gas,
}

/// <summary>
/// The storage archetypes the model operates. Both use the same fleet abstraction and differ
/// only in their assumptions, but storage sizing treats them differently: Battery capacity is
/// what the sizing loop may grow, and pumped hydro is held fixed.
/// </summary>
public enum StorageTechnology
{
    /// <summary>
    /// Grid-scale battery storage. The only technology storage sizing will add capacity to.
    /// </summary>
    Battery,

    /// <summary>
    /// Pumped hydro storage. Held at its scenario capacity; sizing never grows it, because new
    /// pumped hydro is a site-specific project rather than a quantity that can be scaled freely.
    /// </summary>
    PumpedHydro,
}

/// <summary>
/// Technical assumptions for one scenario generation fleet.
/// </summary>
public sealed record GenerationTechnologyProfile
{
    /// <summary>Validates and creates a generation technology profile.</summary>
    /// <param name="heatRate">
    /// Thermal energy consumed per MWh of electricity generated. Fuel cost is charged on gross
    /// generation, not on the energy that reaches load.
    /// </param>
    /// <param name="technicalLifeYears">
    /// Operating life used to annuitise capital cost. Must be positive.
    /// </param>
    /// <param name="emissionsIntensity">
    /// Greenhouse gas released per MWh of electricity generated. Like fuel, this is charged on
    /// gross generation, not on the energy that reaches load. Zero for a fleet that emits nothing
    /// when it runs; there is no technology-name default, so a scenario states it either way.
    /// </param>
    public GenerationTechnologyProfile(
        HeatRate heatRate,
        uint technicalLifeYears,
        GenerationEmissionsIntensity emissionsIntensity)
    {
        if (technicalLifeYears == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(technicalLifeYears));
        }

        HeatRate = heatRate;
        TechnicalLifeYears = technicalLifeYears;
        EmissionsIntensity = emissionsIntensity;
    }

    /// <summary>Thermal energy consumed per MWh of electricity generated.</summary>
    public HeatRate HeatRate { get; }

    /// <summary>Expected operating life of the generating asset in years.</summary>
    public uint TechnicalLifeYears { get; }

    /// <summary>
    /// Operational greenhouse gas released per MWh of electricity generated, in t CO2-e/MWh. This
    /// is combustion only: it excludes fuel extraction, construction and decommissioning, so it is
    /// not a life-cycle figure.
    /// </summary>
    public GenerationEmissionsIntensity EmissionsIntensity { get; }
}

/// <summary>
/// Technical assumptions for one storage fleet. Supplied by the scenario; there are no
/// technology-name defaults in the domain, so a plan that omits these is rejected rather than
/// silently filled in.
/// </summary>
public sealed record StorageTechnologyProfile
{
    /// <summary>Validates and creates a storage technology profile.</summary>
    /// <param name="technicalLifeYears">
    /// Operating life used to annuitise capital cost. Must be positive.
    /// </param>
    /// <param name="roundTripEfficiency">
    /// Fraction of grid energy used to charge that survives a full charge-discharge cycle.
    /// Must be between zero and one inclusive.
    /// </param>
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

    /// <summary>Expected operating life of the storage asset in years.</summary>
    public uint TechnicalLifeYears { get; }

    /// <summary>
    /// Round-trip efficiency as a fraction from zero to one. Applied once, on charging: input
    /// MWh multiplied by this becomes stored MWh, and discharge delivers stored MWh
    /// one-for-one. A full cycle therefore loses <c>1 - RoundTripEfficiency</c> of the grid
    /// energy used to charge.
    /// </summary>
    public double RoundTripEfficiency { get; }
}

