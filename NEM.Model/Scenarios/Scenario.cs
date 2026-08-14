using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Scenarios;

public sealed record ScenarioId
{
    public ScenarioId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed class Scenario
{
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

    public ScenarioId Id { get; }
    public string Name { get; }
    public DateTimeOffset PeriodStart { get; }
    public DateTimeOffset PeriodEnd { get; }
    public IReadOnlyList<ScenarioRegion> Regions { get; }
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

    public string FromRegionId { get; }
    public string ToRegionId { get; }
    public Power Capacity { get; }
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

    public StorageTechnology Technology { get; }
    public Energy InitialEnergyCapacity { get; }
    public Power InitialPowerCapacity { get; }
    public StorageCostParameters CostParameters { get; }
    public StorageTechnologyProfile TechnologyProfile { get; }

    internal StorageFleet? ToStorageFleet() => InitialEnergyCapacity == Energy.Zero
        ? null
        : new StorageFleet(
            Technology,
            InitialEnergyCapacity,
            InitialPowerCapacity,
            TechnologyProfile);
}

public sealed class ScenarioGeneratingFleet
{
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

    public GenerationTechnology Technology { get; }
    public Power NameplateCapacity { get; }
    public GenerationCostParameters CostParameters { get; }
    public GenerationTechnologyProfile TechnologyProfile { get; }
    public IReadOnlyDictionary<DateOnly, double>? MonthlyCapacityFactors { get; }

    internal GeneratingFleet ToGeneratingFleet() =>
        new(
            Technology,
            NameplateCapacity,
            MonthlyCapacityFactors,
            shortRunMarginalCost: CostParameters.ShortRunMarginalCostFor(TechnologyProfile));
}