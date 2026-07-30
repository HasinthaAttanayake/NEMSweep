using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Weather;

namespace NEM.Model.Scenarios;

public static class ScenarioDerivation
{
    public static PowerSystem Derive(
        Scenario scenario,
        FlowSeries baseDemand,
        RegionalResourceProfile? resourceProfile = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(baseDemand);

        FlowSeries hourlyDemand = baseDemand.ResampleToHourly();
        DateTimeOffset demandEnd = hourlyDemand.Start.AddTicks(
            hourlyDemand.Resolution.Ticks * hourlyDemand.Length);
        if (hourlyDemand.Start != scenario.PeriodStart || demandEnd != scenario.PeriodEnd)
        {
            throw new ArgumentException(
                "Demand must align exactly with the scenario period.",
                nameof(baseDemand));
        }

        GeneratingFleet[] fleets = scenario.Fleets
            .Select(fleet => fleet.ToGeneratingFleet())
            .ToArray();
        var region = new Region(
            scenario.RegionId,
            fleets,
            hourlyDemand,
            resourceProfile: resourceProfile);

        return new PowerSystem(
            new PowerSystemId($"{scenario.Id.Value}-system"),
            scenario.Id,
            [region]);
    }
}