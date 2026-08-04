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
    private static readonly CostBasis CostBasis = new(2026, 0.07);
    private static readonly CostParameters CostParameters = new(
        PowerCapacityCost.FromAudPerMwCapacity(1),
        EnergyCapacityCost.FromAudPerMwhStorage(2),
        AnnualPowerCapacityCost.FromAudPerMwYear(3),
        EnergyPrice.FromAudPerMwhDelivered(4),
        FuelPrice.FromAudPerGjThermal(5));

    [Fact]
    public void Derive_RealisesScenarioGeneratingFleetsAndKeepsScenarioIdentity()
    {
        var scenario = new Scenario(
            new ScenarioId("nsw1-baseline"),
            "NSW1 baseline",
            "NSW1",
            Start,
            Start.AddHours(2),
            [new ScenarioGeneratingFleet(
                GenerationTechnology.Coal,
                Power.FromMegawatts(100),
                CostParameters)],
            CostBasis);

        PowerSystem system = ScenarioDerivation.Derive(
            scenario,
            new FlowSeries(Start, TimeSpan.FromMinutes(30), [80, 100, 90, 110]));

        system.Id.Value.Should().Be("nsw1-baseline-system");
        system.DerivedFromScenario.Should().Be(scenario.Id);
        system.Regions.Should().ContainSingle();
        system.Regions[0].RegionId.Should().Be("NSW1");
        system.Regions[0].GeneratingFleets.Should().ContainSingle()
            .Which.NameplateCapacity.Should().Be(Power.FromMegawatts(100));
        system.Regions[0].Demand.TotalDemand[0].Megawatts.Should().Be(90);
        scenario.CostBasis.Should().BeSameAs(CostBasis);
        scenario.GeneratingFleets[0].CostParameters.Should().BeSameAs(CostParameters);
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
            [new ScenarioGeneratingFleet(
                GenerationTechnology.Coal,
                Power.FromMegawatts(100),
                CostParameters)],
            CostBasis);
        var demand = new FlowSeries(Start.AddHours(1), TimeSpan.FromHours(1), [100]);

        var act = () => ScenarioDerivation.Derive(scenario, demand);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("baseDemand")
            .WithMessage("Demand must align exactly with the scenario period.*");
    }
}