using AwesomeAssertions;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Tests.Scenarios;

public sealed class ScenarioDerivationTests
{
    private static readonly DateTimeOffset Start =
        new(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
    private static readonly CostBasis CostBasis = new(2026, 0.07m);
    private static readonly GenerationCostParameters CostParameters = new(
        PowerCapacityCost.FromAudPerMwCapacity(1),
        AnnualPowerCapacityCost.FromAudPerMwYear(3),
        GenerationEnergyCost.FromAudPerMwhGenerated(4),
        FuelPrice.FromAudPerGjThermal(5));
    private static readonly GenerationTechnologyProfile TechnologyProfile = new(
        HeatRate.FromGigajoulesPerMegawattHour(8),
        technicalLifeYears: 40u);

    [Fact]
    public void Derive_RealisesScenarioGeneratingFleetsAndKeepsScenarioIdentity()
    {
        var scenario = new Scenario(
            new ScenarioId("nsw1-baseline"),
            "NSW1 baseline",
            Start,
            Start.AddHours(2),
            [new ScenarioRegion(
                "NSW1",
                [new ScenarioGeneratingFleet(
                    GenerationTechnology.Coal,
                    Power.FromMegawatts(100),
                    CostParameters,
                    TechnologyProfile)])],
            CostBasis);

        PowerSystem system = ScenarioDerivation.Derive(
            scenario,
            new Dictionary<string, FlowSeries>
            {
                ["NSW1"] = new(Start, TimeSpan.FromMinutes(30), [80, 100, 90, 110]),
            });

        system.Id.Value.Should().Be("nsw1-baseline-system");
        system.DerivedFromScenario.Should().Be(scenario.Id);
        system.Regions.Should().ContainSingle();
        system.Regions[0].RegionId.Should().Be("NSW1");
        system.Regions[0].GeneratingFleets.Should().ContainSingle()
            .Which.NameplateCapacity.Should().Be(Power.FromMegawatts(100));
        system.Regions[0].GeneratingFleets[0].ShortRunMarginalCost.Should()
            .Be(GenerationEnergyCost.FromAudPerMwhGenerated(44m));
        system.Regions[0].Demand.TotalDemand[0].Megawatts.Should().Be(90);
        scenario.CostBasis.Should().BeSameAs(CostBasis);
        scenario.Regions[0].GeneratingFleets[0].CostParameters.Should().BeSameAs(CostParameters);
        scenario.Regions[0].GeneratingFleets[0].TechnologyProfile.Should().BeSameAs(TechnologyProfile);
    }

    [Fact]
    public void Derive_RealisesEachRegionalFleetPlanWithRegionSpecificCapacity()
    {
        var scenario = new Scenario(
            new ScenarioId("multi-region"),
            "Multi-region",
            Start,
            Start.AddHours(1),
            [
                new ScenarioRegion("NSW1", [Fleet(GenerationTechnology.Coal, 100)]),
                new ScenarioRegion("QLD1", [Fleet(GenerationTechnology.Coal, 200)]),
            ],
            CostBasis);

        PowerSystem system = ScenarioDerivation.Derive(
            scenario,
            new Dictionary<string, FlowSeries>
            {
                ["NSW1"] = new(Start, TimeSpan.FromHours(1), [80]),
                ["QLD1"] = new(Start, TimeSpan.FromHours(1), [160]),
            });

        system.Regions.Select(region => region.RegionId).Should().Equal("NSW1", "QLD1");
        system.Regions[0].GeneratingFleets.Single().NameplateCapacity.Should()
            .Be(Power.FromMegawatts(100));
        system.Regions[1].GeneratingFleets.Single().NameplateCapacity.Should()
            .Be(Power.FromMegawatts(200));
    }

    [Fact]
    public void Derive_RejectsDemandOutsideScenarioPeriod()
    {
        var scenario = new Scenario(
            new ScenarioId("nsw1-baseline"),
            "NSW1 baseline",
            Start,
            Start.AddHours(1),
            [new ScenarioRegion(
                "NSW1",
                [new ScenarioGeneratingFleet(
                    GenerationTechnology.Coal,
                    Power.FromMegawatts(100),
                    CostParameters,
                    TechnologyProfile)])],
            CostBasis);
        var demand = new FlowSeries(Start.AddHours(1), TimeSpan.FromHours(1), [100]);

        var act = () => ScenarioDerivation.Derive(
            scenario,
            new Dictionary<string, FlowSeries> { ["NSW1"] = demand });

        act.Should().Throw<ArgumentException>()
            .WithParameterName("baseDemandByRegion")
            .WithMessage("Demand must align exactly with the scenario period.*");
    }

    [Fact]
    public void Derive_RealisesOnlyInitiallyInstalledStorage()
    {
        var storageCosts = new StorageCostParameters(
            PowerCapacityCost.FromAudPerMwCapacity(1),
            EnergyCapacityCost.FromAudPerMwhCapacity(2),
            AnnualPowerCapacityCost.FromAudPerMwYear(3));
        var scenario = new Scenario(
            new ScenarioId("storage-plan"),
            "Storage plan",
            Start,
            Start.AddHours(1),
            [new ScenarioRegion(
                "NSW1",
                [Fleet(GenerationTechnology.Coal, 100)],
                [
                    new ScenarioStorageFleet(
                        StorageTechnology.Battery,
                        Energy.Zero,
                        Power.Zero,
                        storageCosts,
                        new StorageTechnologyProfile(15u, 0.87)),
                    new ScenarioStorageFleet(
                        StorageTechnology.PumpedHydro,
                        Energy.FromMegawattHours(800),
                        Power.FromMegawatts(200),
                        storageCosts,
                        new StorageTechnologyProfile(50u, 0.78)),
                ])],
            CostBasis);

        PowerSystem system = ScenarioDerivation.Derive(
            scenario,
            new Dictionary<string, FlowSeries>
            {
                ["NSW1"] = new(Start, TimeSpan.FromHours(1), [80]),
            });

        system.Regions.Single().StorageFleets.Should().ContainSingle().Which
            .StorageTechnology.Should().Be(StorageTechnology.PumpedHydro);
        system.Regions.Single().StorageTechnologyProfiles[StorageTechnology.Battery]
            .Should().Be(new StorageTechnologyProfile(15u, 0.87));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    public void StoragePlan_RejectsUnpairedInitialCapacity(
        double energyCapacityMwh,
        double powerCapacityMw)
    {
        var act = () => new ScenarioStorageFleet(
            StorageTechnology.Battery,
            Energy.FromMegawattHours(energyCapacityMwh),
            Power.FromMegawatts(powerCapacityMw),
            ZeroStorageCosts(),
            new StorageTechnologyProfile(15u, 0.87));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ScenarioRegion_RejectsDuplicateStorageTechnologies()
    {
        ScenarioStorageFleet battery = new(
            StorageTechnology.Battery,
            Energy.Zero,
            Power.Zero,
            ZeroStorageCosts(),
            new StorageTechnologyProfile(15u, 0.87));

        var act = () => new ScenarioRegion(
            "NSW1",
            [Fleet(GenerationTechnology.Coal, 100)],
            [battery, battery]);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("storageFleets");
    }

    private static StorageCostParameters ZeroStorageCosts() => new(
        PowerCapacityCost.FromAudPerMwCapacity(0),
        EnergyCapacityCost.FromAudPerMwhCapacity(0),
        AnnualPowerCapacityCost.FromAudPerMwYear(0));

    private static ScenarioGeneratingFleet Fleet(
        GenerationTechnology technology,
        double nameplateCapacityMw) =>
        new(
            technology,
            Power.FromMegawatts(nameplateCapacityMw),
            CostParameters,
            TechnologyProfile);
}