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
        IReadOnlyList<ScenarioFleet> fleets)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        ArgumentNullException.ThrowIfNull(fleets);
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

        if (fleets.Count == 0 || fleets.Any(fleet => fleet is null))
        {
            throw new ArgumentException(
                "A scenario must contain at least one non-null fleet plan.",
                nameof(fleets));
        }

        if (fleets.DistinctBy(fleet => fleet.Technology).Count() != fleets.Count)
        {
            throw new ArgumentException(
                "A scenario cannot contain duplicate fleet technologies.",
                nameof(fleets));
        }

        Id = id;
        Name = name;
        RegionId = regionId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Fleets = Array.AsReadOnly(fleets.ToArray());
    }

    public ScenarioId Id { get; }
    public string Name { get; }
    public string RegionId { get; }
    public DateTimeOffset PeriodStart { get; }
    public DateTimeOffset PeriodEnd { get; }
    public IReadOnlyList<ScenarioFleet> Fleets { get; }
}

public sealed class ScenarioFleet
{
    public ScenarioFleet(
        GenerationTechnology technology,
        Power nameplateCapacity,
        IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors = null)
    {
        if (nameplateCapacity < Power.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(nameplateCapacity));
        }

        Technology = technology;
        NameplateCapacity = nameplateCapacity;
        MonthlyCapacityFactors = monthlyCapacityFactors is null
            ? null
            : new ReadOnlyDictionary<DateOnly, double>(
                new Dictionary<DateOnly, double>(monthlyCapacityFactors));

        _ = ToGeneratingFleet();
    }

    public GenerationTechnology Technology { get; }
    public Power NameplateCapacity { get; }
    public IReadOnlyDictionary<DateOnly, double>? MonthlyCapacityFactors { get; }

    internal GeneratingFleet ToGeneratingFleet() =>
        new(Technology, NameplateCapacity, MonthlyCapacityFactors);
}