using NEMSweep.Model.Grid;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Economics;

/// <summary>Operational emissions attributable to one generation technology.</summary>
public sealed record GenerationEmissionsContribution(
    GenerationTechnology Technology,
    Emissions Emissions);

/// <summary>
/// Annual operational emissions for one region, using that region's energy served as the
/// denominator for its emissions intensity.
/// </summary>
public sealed record RegionEmissionsSummary
{
    /// <summary>
    /// Relative tolerance for the contributions-sum invariant. Emissions are a floating-point
    /// measurement rather than currency, so summing a technology at a time and summing across
    /// regions need not produce bit-identical totals; anything beyond this is a real defect.
    /// </summary>
    internal const double ReconciliationTolerance = 1e-9;

    internal RegionEmissionsSummary(
        string regionId,
        Emissions totalEmissions,
        Energy energyServed,
        IReadOnlyList<GenerationEmissionsContribution> generationEmissionsContributions)
    {
        RegionId = regionId;
        TotalEmissions = totalEmissions;
        GenerationEmissionsContributions = generationEmissionsContributions.ToArray();
        double contributionTotal = GenerationEmissionsContributions.Sum(
            contribution => contribution.Emissions.TonnesCO2e);
        if (Math.Abs(contributionTotal - totalEmissions.TonnesCO2e)
            > ReconciliationTolerance * Math.Max(1, contributionTotal))
        {
            throw new ArgumentException(
                "Generation emissions contributions must sum to total emissions.",
                nameof(generationEmissionsContributions));
        }

        if (!double.IsFinite(energyServed.MegawattHours) || energyServed.MegawattHours <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(energyServed),
                "Energy served must be positive when deriving an emissions intensity for region "
                + $"'{regionId}'.");
        }

        EnergyServed = energyServed;
        EmissionsIntensity = totalEmissions.Per(energyServed);
    }

    /// <summary>Region identifier corresponding to the scenario and dispatch outcome.</summary>
    public string RegionId { get; }

    /// <summary>Annual operational emissions from generation in this region, in t CO2-e.</summary>
    public Emissions TotalEmissions { get; }

    /// <summary>Annual emissions by technology, in t CO2-e. Sums to <see cref="TotalEmissions"/>.</summary>
    public IReadOnlyList<GenerationEmissionsContribution> GenerationEmissionsContributions { get; }

    /// <summary>Annual energy served in this region, in MWh. The intensity denominator.</summary>
    public Energy EnergyServed { get; }

    /// <summary>
    /// Regional emissions per MWh served, in t CO2-e/MWh. Emissions are attributed to the region
    /// whose plant burned the fuel, while the denominator is the load that region served, so a net
    /// importer's figure understates the emissions behind its supply and a net exporter's
    /// overstates them. <see cref="EmissionsSummary.SystemEmissionsIntensity"/> is the figure free
    /// of that distortion.
    /// </summary>
    public ServedEmissionsIntensity EmissionsIntensity { get; }
}

/// <summary>
/// Annual operational emissions for a dispatched system, by region and by technology.
/// </summary>
/// <remarks>
/// <para>
/// These are combustion emissions from generation only. They exclude fuel extraction and delivery,
/// construction, and decommissioning, so this is an operational figure and not a life-cycle one.
/// </para>
/// <para>
/// Emissions are charged on gross generation, the same basis as fuel cost, so energy a fleet
/// generated to charge storage carries the emissions of generating it. That is what makes a
/// battery's round-trip loss show up in the system intensity rather than disappear: more had to be
/// generated than was later delivered, and the difference is accounted where it was burned.
/// </para>
/// </remarks>
public sealed record EmissionsSummary
{
    internal EmissionsSummary(
        Emissions totalEmissions,
        Energy energyServed,
        IReadOnlyList<RegionEmissionsSummary> regions,
        IReadOnlyList<GenerationEmissionsContribution> generationEmissionsContributions)
    {
        Regions = regions.ToArray();
        GenerationEmissionsContributions = generationEmissionsContributions.ToArray();
        double contributionTotal = GenerationEmissionsContributions.Sum(
            contribution => contribution.Emissions.TonnesCO2e);
        if (Math.Abs(contributionTotal - totalEmissions.TonnesCO2e)
            > RegionEmissionsSummary.ReconciliationTolerance * Math.Max(1, contributionTotal))
        {
            throw new ArgumentException(
                "Generation emissions contributions must sum to total emissions.",
                nameof(generationEmissionsContributions));
        }

        TotalEmissions = totalEmissions;
        EnergyServed = energyServed;
        SystemEmissionsIntensity = totalEmissions.Per(energyServed);
    }

    /// <summary>Annual operational emissions across every region, in t CO2-e.</summary>
    public Emissions TotalEmissions { get; }

    /// <summary>Annual energy served across every region, in MWh. The intensity denominator.</summary>
    public Energy EnergyServed { get; }

    /// <summary>
    /// System emissions per MWh served, in t CO2-e/MWh. Unlike the regional figures this is not
    /// distorted by interregional transfer, because both the emissions and the load are summed
    /// across the whole system.
    /// </summary>
    public ServedEmissionsIntensity SystemEmissionsIntensity { get; }

    /// <summary>Regional emissions and intensities, one entry per system region.</summary>
    public IReadOnlyList<RegionEmissionsSummary> Regions { get; }

    /// <summary>System emissions aggregated by technology, in t CO2-e.</summary>
    public IReadOnlyList<GenerationEmissionsContribution> GenerationEmissionsContributions { get; }
}
