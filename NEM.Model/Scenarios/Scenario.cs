using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Scenarios;

/// <summary>
/// Stable identity for a scenario. A result cites this rather than serialising the scenario
/// object graph, so runs stay comparable by identity without comparing inputs field by field.
/// </summary>
public sealed record ScenarioId
{
    /// <summary>Creates a scenario identity from a non-blank string.</summary>
    /// <param name="value">The identifier, typically the scenario config's <c>id</c> field.</param>
    public ScenarioId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The identifier as supplied.</summary>
    public string Value { get; }

    /// <summary>Returns <see cref="Value"/>.</summary>
    public override string ToString() => Value;
}

/// <summary>
/// The aggregate root for scenario intent: what a user asks the model to simulate, before any of
/// it is realised against data. It owns the modelled period, one fleet plan per region, the cost
/// basis, and any directed interconnectors.
/// </summary>
/// <remarks>
/// A scenario is intent, not run configuration. Demand and weather inputs are located by CLI
/// settings and are deliberately not part of scenario identity; a result records the filename,
/// schema version and SHA-256 of the exact bytes it parsed instead.
/// <see cref="ScenarioDerivation.Derive"/> turns this intent plus aligned demand into a realised
/// <see cref="NEM.Model.Grid.PowerSystem"/>.
/// </remarks>
public sealed class Scenario
{
    /// <summary>Validates and creates a scenario.</summary>
    /// <param name="id">Stable identity cited by every result derived from this scenario.</param>
    /// <param name="name">Human-readable name. Must not be blank.</param>
    /// <param name="periodStart">Inclusive period start in NEM market time (UTC+10).</param>
    /// <param name="periodEnd">Exclusive period end in NEM market time (UTC+10).</param>
    /// <param name="regions">
    /// At least one regional plan, with distinct region IDs compared case-insensitively.
    /// </param>
    /// <param name="costBasis">The real-dollar year and real discount rate applied to every cost.</param>
    /// <param name="interconnectors">
    /// Directed transfer paths, at most one per exact direction, whose endpoints must both be
    /// regions of this scenario. Null or empty models the regions as unlinked.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A period bound is not in UTC+10, the regions are empty or contain duplicates, or an
    /// interconnector names an unknown endpoint or repeats a direction.
    /// </exception>
    public Scenario(
        ScenarioId id,
        string name,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        IReadOnlyList<ScenarioRegion> regions,
        CostBasis costBasis,
        IReadOnlyList<ScenarioInterconnector>? interconnectors = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(costBasis);
        if (periodStart.Offset != TimeSpan.FromHours(10)
            || periodEnd.Offset != TimeSpan.FromHours(10))
        {
            throw new ArgumentException("Scenario periods must use NEM market time (UTC+10).");
        }

        if (periodEnd <= periodStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodEnd),
                periodEnd,
                "Scenario period end must be after its start.");
        }

        if (regions.Count == 0 || regions.Any(region => region is null))
        {
            throw new ArgumentException(
                "A scenario must contain at least one non-null regional plan.",
                nameof(regions));
        }

        if (regions.DistinctBy(region => region.RegionId, StringComparer.OrdinalIgnoreCase).Count()
            != regions.Count)
        {
            throw new ArgumentException(
                "A scenario cannot contain duplicate region IDs.",
                nameof(regions));
        }

        ScenarioInterconnector[] links = interconnectors?.ToArray() ?? [];
        ValidateInterconnectors(regions, links);

        Id = id;
        Name = name;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Regions = Array.AsReadOnly(regions.ToArray());
        CostBasis = costBasis;
        Interconnectors = Array.AsReadOnly(links);
    }

    /// <summary>Stable identity cited by every result derived from this scenario.</summary>
    public ScenarioId Id { get; }

    /// <summary>Human-readable scenario name.</summary>
    public string Name { get; }

    /// <summary>Inclusive period start, in NEM market time (UTC+10).</summary>
    public DateTimeOffset PeriodStart { get; }

    /// <summary>Exclusive period end, in NEM market time (UTC+10).</summary>
    public DateTimeOffset PeriodEnd { get; }

    /// <summary>One fleet plan per region, with distinct region IDs.</summary>
    public IReadOnlyList<ScenarioRegion> Regions { get; }

    /// <summary>The real-dollar year and real discount rate applied to every cost in this scenario.</summary>
    public CostBasis CostBasis { get; }

    /// <summary>
    /// Directed transfer intent between regions. Cross-regional by nature, so it hangs
    /// off the scenario alongside <see cref="CostBasis"/> rather than off any single region.
    /// </summary>
    public IReadOnlyList<ScenarioInterconnector> Interconnectors { get; }

    private static void ValidateInterconnectors(
        IReadOnlyList<ScenarioRegion> regions,
        IReadOnlyList<ScenarioInterconnector> interconnectors)
    {
        if (interconnectors.Any(interconnector => interconnector is null))
        {
            throw new ArgumentException(
                "Scenario interconnector plans cannot contain null.",
                nameof(interconnectors));
        }

        var regionIds = regions
            .Select(region => region.RegionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directions = new HashSet<(string From, string To)>();
        foreach (ScenarioInterconnector interconnector in interconnectors)
        {
            foreach (string endpoint in
                new[] { interconnector.FromRegionId, interconnector.ToRegionId })
            {
                if (!regionIds.Contains(endpoint))
                {
                    throw new ArgumentException(
                        $"Interconnector endpoint '{endpoint}' is not a region of this scenario.",
                        nameof(interconnectors));
                }
            }

            var direction = (
                interconnector.FromRegionId.ToUpperInvariant(),
                interconnector.ToRegionId.ToUpperInvariant());
            if (!directions.Add(direction))
            {
                throw new ArgumentException(
                    "A scenario cannot contain duplicate interconnectors from "
                    + $"'{interconnector.FromRegionId}' to '{interconnector.ToRegionId}'.",
                    nameof(interconnectors));
            }
        }
    }
}

/// <summary>Scenario intent for one directed transfer path between two regions.</summary>
public sealed class ScenarioInterconnector
{
    /// <summary>Validates and creates one directed transfer plan.</summary>
    /// <param name="fromRegionId">Sending region. Transfer capacity is metered at this end.</param>
    /// <param name="toRegionId">Receiving region. Must differ from <paramref name="fromRegionId"/>.</param>
    /// <param name="capacity">Directed transfer capacity in MW. A reciprocal path is a separate plan.</param>
    /// <param name="costParameters">Capital and fixed operating cost per km per MW of capacity.</param>
    /// <param name="technicalLifeYears">Asset life used to annuitise capital cost. Must be positive.</param>
    public ScenarioInterconnector(
        string fromRegionId,
        string toRegionId,
        Power capacity,
        TransmissionCostParameters costParameters,
        uint technicalLifeYears)
    {
        ArgumentNullException.ThrowIfNull(costParameters);
        ArgumentOutOfRangeException.ThrowIfZero(technicalLifeYears);

        // Validated here so endpoint and capacity rules live in exactly one place.
        Interconnector realised = new(fromRegionId, toRegionId, capacity);

        FromRegionId = realised.FromRegionId;
        ToRegionId = realised.ToRegionId;
        Capacity = realised.Capacity;
        CostParameters = costParameters;
        TechnicalLifeYears = technicalLifeYears;
    }

    /// <summary>Sending region. Transfer capacity is metered at this end.</summary>
    public string FromRegionId { get; }

    /// <summary>Receiving region.</summary>
    public string ToRegionId { get; }

    /// <summary>Directed transfer capacity in MW.</summary>
    public Power Capacity { get; }

    /// <summary>
    /// Capital and fixed operating cost per km per MW. Route length is derived from the endpoint
    /// regions' weather-site coordinates rather than declared here.
    /// </summary>
    public TransmissionCostParameters CostParameters { get; }

    /// <summary>
    /// Asset life used to annuitise capital cost. Held directly rather than on a profile
    /// type, because it is the only technical parameter an interconnector carries.
    /// </summary>
    public uint TechnicalLifeYears { get; }

    /// <summary>Realises the scenario intent as a grid asset.</summary>
    public Interconnector ToInterconnector() =>
        new(FromRegionId, ToRegionId, Capacity);
}

/// <summary>Scenario intent for the fleet plan in one NEM region.</summary>
public sealed class ScenarioRegion
{
    /// <summary>Validates and creates one region's fleet plan.</summary>
    /// <param name="regionId">The NEM region this plan realises, for example <c>NSW1</c>.</param>
    /// <param name="generatingFleets">At least one plan, with distinct generation technologies.</param>
    /// <param name="storageFleets">
    /// Storage plans with distinct storage technologies, or null for a region carrying no storage
    /// assumptions. A zero-capacity plan is retained as assumptions for capacity that storage
    /// sizing may later introduce.
    /// </param>
    public ScenarioRegion(
        string regionId,
        IReadOnlyList<ScenarioGeneratingFleet> generatingFleets,
        IReadOnlyList<ScenarioStorageFleet>? storageFleets = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        ArgumentNullException.ThrowIfNull(generatingFleets);
        if (generatingFleets.Count == 0 || generatingFleets.Any(fleet => fleet is null))
        {
            throw new ArgumentException(
                "A scenario region must contain at least one non-null generating fleet plan.",
                nameof(generatingFleets));
        }

        if (generatingFleets.DistinctBy(fleet => fleet.Technology).Count()
            != generatingFleets.Count)
        {
            throw new ArgumentException(
                "A scenario region cannot contain duplicate generating fleet technologies.",
                nameof(generatingFleets));
        }

        IReadOnlyList<ScenarioStorageFleet> resolvedStorageFleets = storageFleets ?? [];
        if (resolvedStorageFleets.Any(fleet => fleet is null))
        {
            throw new ArgumentException(
                "Scenario storage fleet plans cannot contain null.",
                nameof(storageFleets));
        }

        if (resolvedStorageFleets.DistinctBy(fleet => fleet.Technology).Count()
            != resolvedStorageFleets.Count)
        {
            throw new ArgumentException(
                "A scenario region cannot contain duplicate storage fleet technologies.",
                nameof(storageFleets));
        }

        RegionId = regionId;
        GeneratingFleets = Array.AsReadOnly(generatingFleets.ToArray());
        StorageFleets = Array.AsReadOnly(resolvedStorageFleets.ToArray());
    }

    /// <summary>Identifies the NEM region to be realised from this plan.</summary>
    public string RegionId { get; }

    /// <summary>One capacity and technology plan for each generation technology in the region.</summary>
    public IReadOnlyList<ScenarioGeneratingFleet> GeneratingFleets { get; }

    /// <summary>Storage capacity and economic assumptions available to the scenario.</summary>
    public IReadOnlyList<ScenarioStorageFleet> StorageFleets { get; }
}

/// <summary>
/// Scenario storage intent, economics, and technical behavior. Zero initial
/// capacities retain assumptions for storage that may be introduced by sizing.
/// </summary>
public sealed class ScenarioStorageFleet
{
    /// <summary>Validates and creates one storage plan.</summary>
    /// <param name="technology">The storage archetype this plan describes.</param>
    /// <param name="initialEnergyCapacity">
    /// Installed storage energy in MWh. Must be zero or positive, and zero exactly when
    /// <paramref name="initialPowerCapacity"/> is zero.
    /// </param>
    /// <param name="initialPowerCapacity">Installed charge and discharge power in MW.</param>
    /// <param name="costParameters">Power capex, energy capex, and fixed operating cost.</param>
    /// <param name="technologyProfile">Technical life and round-trip efficiency.</param>
    /// <exception cref="ArgumentException">
    /// One capacity is zero while the other is positive. A plan is either fully installed or a
    /// pure assumptions placeholder; it cannot be half of each.
    /// </exception>
    public ScenarioStorageFleet(
        StorageTechnology technology,
        Energy initialEnergyCapacity,
        Power initialPowerCapacity,
        StorageCostParameters costParameters,
        StorageTechnologyProfile technologyProfile)
    {
        ArgumentNullException.ThrowIfNull(costParameters);
        ArgumentNullException.ThrowIfNull(technologyProfile);
        if (initialEnergyCapacity < Energy.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialEnergyCapacity));
        }

        if (initialPowerCapacity < Power.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialPowerCapacity));
        }

        if ((initialEnergyCapacity == Energy.Zero) != (initialPowerCapacity == Power.Zero))
        {
            throw new ArgumentException(
                "Initial storage energy and power capacity must either both be zero or both be positive.");
        }

        Technology = technology;
        InitialEnergyCapacity = initialEnergyCapacity;
        InitialPowerCapacity = initialPowerCapacity;
        CostParameters = costParameters;
        TechnologyProfile = technologyProfile;
    }

    /// <summary>The storage archetype this plan describes.</summary>
    public StorageTechnology Technology { get; }

    /// <summary>
    /// Installed storage energy in MWh. Zero realises no fleet, but the cost and technology
    /// assumptions on this plan still govern any capacity storage sizing introduces.
    /// </summary>
    public Energy InitialEnergyCapacity { get; }

    /// <summary>Installed charge and discharge power in MW.</summary>
    public Power InitialPowerCapacity { get; }

    /// <summary>Power capex in AUD/MW, energy capex in AUD/MWh, and fixed operating cost in AUD/MW/year.</summary>
    public StorageCostParameters CostParameters { get; }

    /// <summary>Technical life in years and round-trip efficiency.</summary>
    public StorageTechnologyProfile TechnologyProfile { get; }

    internal StorageFleet? ToStorageFleet() => InitialEnergyCapacity == Energy.Zero
        ? null
        : new StorageFleet(
            Technology,
            InitialEnergyCapacity,
            InitialPowerCapacity,
            TechnologyProfile,
            StorageSeedPolicy.SeedFor(Technology, InitialEnergyCapacity));
}

/// <summary>
/// Scenario intent for one generation technology in one region: how much capacity is assumed,
/// what it costs, and how it behaves. Dispatch-relevant short-run marginal cost is derived from
/// the cost parameters and technology profile rather than declared.
/// </summary>
public sealed class ScenarioGeneratingFleet
{
    /// <summary>Validates and creates one generating fleet plan.</summary>
    /// <param name="technology">The generation technology this plan describes.</param>
    /// <param name="nameplateCapacity">Installed nameplate capacity in MW. Must not be negative.</param>
    /// <param name="costParameters">Capital, fixed operating, variable operating and fuel cost.</param>
    /// <param name="technologyProfile">Heat rate and technical life.</param>
    /// <param name="monthlyCapacityFactors">
    /// Optional energy budget expressed as a capacity factor per month, keyed by the first day of
    /// each month. Used by Hydro, whose output is limited by inflow rather than by fuel cost.
    /// </param>
    public ScenarioGeneratingFleet(
        GenerationTechnology technology,
        Power nameplateCapacity,
        GenerationCostParameters costParameters,
        GenerationTechnologyProfile technologyProfile,
        IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors = null)
    {
        ArgumentNullException.ThrowIfNull(costParameters);
        ArgumentNullException.ThrowIfNull(technologyProfile);

        if (nameplateCapacity < Power.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(nameplateCapacity));
        }

        Technology = technology;
        NameplateCapacity = nameplateCapacity;
        CostParameters = costParameters;
        TechnologyProfile = technologyProfile;
        MonthlyCapacityFactors = monthlyCapacityFactors is null
            ? null
            : new ReadOnlyDictionary<DateOnly, double>(
                new Dictionary<DateOnly, double>(monthlyCapacityFactors));

        _ = ToGeneratingFleet();
    }

    /// <summary>The generation technology this plan describes.</summary>
    public GenerationTechnology Technology { get; }

    /// <summary>Installed nameplate capacity in MW.</summary>
    public Power NameplateCapacity { get; }

    /// <summary>Capital cost, fixed operating cost, variable operating cost and fuel price.</summary>
    public GenerationCostParameters CostParameters { get; }

    /// <summary>Heat rate and technical life.</summary>
    public GenerationTechnologyProfile TechnologyProfile { get; }

    /// <summary>
    /// Energy budget as a capacity factor per month, keyed by the first day of the month, or null
    /// when the technology is limited by capacity rather than by an energy allowance.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, double>? MonthlyCapacityFactors { get; }

    internal GeneratingFleet ToGeneratingFleet() =>
        new(
            Technology,
            NameplateCapacity,
            MonthlyCapacityFactors,
            shortRunMarginalCost: CostParameters.ShortRunMarginalCostFor(TechnologyProfile));
}