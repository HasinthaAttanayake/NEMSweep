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
        string regionId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        IReadOnlyList<ScenarioGeneratingFleet> generatingFleets,
        CostBasis costBasis)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        ArgumentNullException.ThrowIfNull(generatingFleets);
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

        if (generatingFleets.Count == 0 || generatingFleets.Any(fleet => fleet is null))
        {
            throw new ArgumentException(
                "A scenario must contain at least one non-null generating fleet plan.",
                nameof(generatingFleets));
        }

        if (generatingFleets.DistinctBy(fleet => fleet.Technology).Count()
            != generatingFleets.Count)
        {
            throw new ArgumentException(
                "A scenario cannot contain duplicate generating fleet technologies.",
                nameof(generatingFleets));
        }

        Id = id;
        Name = name;
        RegionId = regionId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        GeneratingFleets = Array.AsReadOnly(generatingFleets.ToArray());
        CostBasis = costBasis;
    }

    public ScenarioId Id { get; }
    public string Name { get; }
    public string RegionId { get; }
    public DateTimeOffset PeriodStart { get; }
    public DateTimeOffset PeriodEnd { get; }
    public IReadOnlyList<ScenarioGeneratingFleet> GeneratingFleets { get; }
    public CostBasis CostBasis { get; }
}

public sealed class ScenarioGeneratingFleet
{
    public ScenarioGeneratingFleet(
        GenerationTechnology technology,
        Power nameplateCapacity,
        CostParameters costParameters,
        IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors = null)
    {
        ArgumentNullException.ThrowIfNull(costParameters);

        if (nameplateCapacity < Power.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(nameplateCapacity));
        }

        Technology = technology;
        NameplateCapacity = nameplateCapacity;
        CostParameters = costParameters;
        MonthlyCapacityFactors = monthlyCapacityFactors is null
            ? null
            : new ReadOnlyDictionary<DateOnly, double>(
                new Dictionary<DateOnly, double>(monthlyCapacityFactors));

        _ = ToGeneratingFleet();
    }

    public GenerationTechnology Technology { get; }
    public Power NameplateCapacity { get; }
    public CostParameters CostParameters { get; }
    public IReadOnlyDictionary<DateOnly, double>? MonthlyCapacityFactors { get; }

    internal GeneratingFleet ToGeneratingFleet() =>
        new(Technology, NameplateCapacity, MonthlyCapacityFactors);
}