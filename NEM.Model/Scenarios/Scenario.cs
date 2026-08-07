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
        CostBasis costBasis)
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

        Id = id;
        Name = name;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Regions = Array.AsReadOnly(regions.ToArray());
        CostBasis = costBasis;
    }

    public ScenarioId Id { get; }
    public string Name { get; }
    public DateTimeOffset PeriodStart { get; }
    public DateTimeOffset PeriodEnd { get; }
    public IReadOnlyList<ScenarioRegion> Regions { get; }
    public CostBasis CostBasis { get; }
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