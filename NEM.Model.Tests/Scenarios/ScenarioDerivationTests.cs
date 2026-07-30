using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Tests.Scenarios;

public sealed class ScenarioDerivationTests
{
    private static readonly DateTimeOffset Start =
        new(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Derive_RealisesScenarioFleetsAndKeepsScenarioIdentity()
    {
        var scenario = new Scenario(
            new ScenarioId("nsw1-baseline"),
            "NSW1 baseline",
            "NSW1",
            Start,
            Start.AddHours(2),
            [new ScenarioFleet(TechnologyKey.Coal, Power.FromMegawatts(100))]);

        PowerSystem system = ScenarioDerivation.Derive(
            scenario,
            new FlowSeries(Start, TimeSpan.FromMinutes(30), [80, 100, 90, 110]));

        system.Id.Value.Should().Be("nsw1-baseline-system");
        system.DerivedFromScenario.Should().Be(scenario.Id);
        system.Regions.Should().ContainSingle();
        system.Regions[0].RegionId.Should().Be("NSW1");
        system.Regions[0].Fleets.Should().ContainSingle()
            .Which.NameplateCapacity.Should().Be(Power.FromMegawatts(100));
        system.Regions[0].Demand.TotalDemand[0].Megawatts.Should().Be(90);
    }

    [Fact]
    public void Derive_RejectsDemandOutsideScenarioPeriod()
    {
        var scenario = new Scenario(
            new ScenarioId("nsw1-baseline"),
            "NSW1 baseline",
            "NSW1",
            Start,
            Start.AddHours(1),
            [new ScenarioFleet(TechnologyKey.Coal, Power.FromMegawatts(100))]);
        var demand = new FlowSeries(Start.AddHours(1), TimeSpan.FromHours(1), [100]);

        var act = () => ScenarioDerivation.Derive(scenario, demand);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("baseDemand")
            .WithMessage("Demand must align exactly with the scenario period.*");
    }
}