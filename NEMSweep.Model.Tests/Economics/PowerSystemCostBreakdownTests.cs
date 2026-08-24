using AwesomeAssertions;
using NEMSweep.Model.Economics;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.StorageSizing;
using NEMSweep.Model.Units;
using NEMSweep.Model.Weather;

namespace NEMSweep.Model.Tests.Economics;

public sealed class PowerSystemCostBreakdownTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void UsesGrossGenerationForVariableAndFuelCostWhenChargingStorage()
    {
        const decimal capitalCostPerMw = 1_000m;
        const decimal fixedOperatingCostPerMwYear = 100m;
        const decimal variableOperatingCostPerMwh = 4m;
        const decimal fuelPricePerGj = 2m;
        const double heatRateGjPerMwh = 3;
        Power nameplateCapacity = Power.FromMegawatts(10);
        Energy deliveredEnergy = Energy.FromMegawattHours(180);
        Money totalCapex = Money.FromAud(capitalCostPerMw * 10);
        Money annualisedCapex = LevelisedCostCalculator.Annuitise(totalCapex, 0m, 10);
        Money annualFixedOpex = Money.FromAud(fixedOperatingCostPerMwYear * 10);
        Money annualVariableOpex = Money.FromAud(variableOperatingCostPerMwh * 190m);
        Money annualFuelCost = Money.FromAud(fuelPricePerGj * 3m * 190m);
        Money expectedAnnualCost = annualisedCapex
            + annualFixedOpex
            + annualVariableOpex
            + annualFuelCost;

        var scenario = new Scenario(
            new ScenarioId("cost-breakdown-scenario"),
            "Cost breakdown scenario",
            Start,
            Start.AddYears(1),
            [new ScenarioRegion(
                "NSW1",
                [new ScenarioGeneratingFleet(
                    GenerationTechnology.Gas,
                    nameplateCapacity,
                    new GenerationCostParameters(
                        PowerCapacityCost.FromAudPerMwCapacity(capitalCostPerMw),
                        AnnualPowerCapacityCost.FromAudPerMwYear(fixedOperatingCostPerMwYear),
                        GenerationEnergyCost.FromAudPerMwhGenerated(variableOperatingCostPerMwh),
                        FuelPrice.FromAudPerGjThermal(fuelPricePerGj)),
                    new GenerationTechnologyProfile(
                        HeatRate.FromGigajoulesPerMegawattHour(heatRateGjPerMwh),
                        technicalLifeYears: 10))])],
            new CostBasis(2026, realDiscountRate: 0));

        PowerSystemCostBreakdown breakdown = PowerSystemCostCalculator.Calculate(
            scenario,
            RunResult(DispatchOutcomeWithCurtailment()));

        breakdown.DeliveredEnergy.Should().Be(deliveredEnergy,
            "load served through storage discharge must remain in the system denominator");
        breakdown.TotalAnnualisedCost.Should().Be(expectedAnnualCost);
        breakdown.TotalAnnualisedGenerationCost.Should().Be(expectedAnnualCost,
            "the 190 MWh gross generation includes 40 MWh used to charge storage");
        breakdown.SystemLevelisedCostOfGeneration.Should()
            .Be(expectedAnnualCost.Per(deliveredEnergy));
        breakdown.SystemLevelisedCostOfElectricity.Should()
            .Be(expectedAnnualCost.Per(deliveredEnergy));
        breakdown.SystemLevelisedCostOfStorage.Should().Be(default(EnergyPrice));
        breakdown.SystemLevelisedCostOfTransmission.Should().Be(default(EnergyPrice));
    }

    [Fact]
    public void IncludesAnnualisedStorageAssetCostWithoutChargingEnergyDoubleCount()
    {
        const decimal powerCapitalCostPerMw = 1_000m;
        const decimal energyCapitalCostPerMwh = 200m;
        const decimal fixedOperatingCostPerMwYear = 10m;
        Power storagePower = Power.FromMegawatts(10);
        Energy storageEnergy = Energy.FromMegawattHours(40);
        Energy deliveredEnergy = Energy.FromMegawattHours(180);
        Money storageCapex = Money.FromAud(
            (powerCapitalCostPerMw * 10) + (energyCapitalCostPerMwh * 40));
        Money expectedAnnualStorageCost = LevelisedCostCalculator.Annuitise(
            storageCapex,
            rate: 0m,
            years: 10)
            + Money.FromAud(fixedOperatingCostPerMwYear * 10);
        Scenario scenario = MinimalScenario(
            Start.AddYears(1),
            storageFleets:
            [
                new ScenarioStorageFleet(
                    StorageTechnology.Battery,
                    Energy.Zero,
                    Power.Zero,
                    new StorageCostParameters(
                        PowerCapacityCost.FromAudPerMwCapacity(powerCapitalCostPerMw),
                        EnergyCapacityCost.FromAudPerMwhCapacity(energyCapitalCostPerMwh),
                        AnnualPowerCapacityCost.FromAudPerMwYear(fixedOperatingCostPerMwYear)),
                    new StorageTechnologyProfile(10u, 0.87)),
            ]);

        PowerSystemCostBreakdown breakdown = PowerSystemCostCalculator.Calculate(
            scenario,
            RunResult(DispatchOutcomeWithCurtailment(), storageEnergy, storagePower));

        breakdown.TotalAnnualisedStorageCost.Should().Be(expectedAnnualStorageCost);
        breakdown.SystemLevelisedCostOfStorage.Should()
            .Be(expectedAnnualStorageCost.Per(deliveredEnergy));
        breakdown.TotalAnnualisedCost.Should().Be(
            breakdown.TotalAnnualisedGenerationCost + expectedAnnualStorageCost);
        breakdown.SystemLevelisedCostOfElectricity.Should().Be(
            breakdown.TotalAnnualisedCost.Per(deliveredEnergy));
    }

    [Fact]
    public void Calculate_SeparatesRegionalCostsUsingEachRegionsDeliveredEnergy()
    {
        Scenario scenario = TwoRegionScenario();
        DispatchOutcome nswOutcome = DispatchOutcomeFor("NSW1", deliveredMegawattHours: 1);
        DispatchOutcome vicOutcome = DispatchOutcomeFor("VIC1", deliveredMegawattHours: 2);
        var powerSystem = new PowerSystem(
            new PowerSystemId("two-region-cost-system"),
            scenario.Id,
            [
                RegionFor("NSW1", nswOutcome.Demand),
                RegionFor("VIC1", vicOutcome.Demand),
            ]);

        PowerSystemCostBreakdown breakdown = PowerSystemCostCalculator.Calculate(
            scenario,
            powerSystem,
            [nswOutcome, vicOutcome]);

        RegionCostBreakdown nsw = breakdown.Regions.Single(region => region.RegionId == "NSW1");
        RegionCostBreakdown vic = breakdown.Regions.Single(region => region.RegionId == "VIC1");

        breakdown.Regions.Should().HaveCount(2);
        breakdown.TotalAnnualisedGenerationCost.Should().Be(
            nsw.AnnualisedGenerationCost + vic.AnnualisedGenerationCost);
        nsw.GenerationCostContributions.Aggregate(
                Money.Zero,
                (total, contribution) => total + contribution.AnnualisedCost)
            .Should().Be(nsw.AnnualisedGenerationCost);
        vic.GenerationCostContributions.Aggregate(
                Money.Zero,
                (total, contribution) => total + contribution.AnnualisedCost)
            .Should().Be(vic.AnnualisedGenerationCost);
        breakdown.GenerationCostContributions.Aggregate(
                Money.Zero,
                (total, contribution) => total + contribution.AnnualisedCost)
            .Should().Be(breakdown.TotalAnnualisedGenerationCost);
        breakdown.GenerationCostContributions.Select(contribution => contribution.Technology)
            .Should().Equal(GenerationTechnology.Gas);
        breakdown.TotalAnnualisedStorageCost.Should().Be(
            nsw.AnnualisedStorageCost + vic.AnnualisedStorageCost);
        breakdown.TotalAnnualisedCost.Should().Be(
            nsw.TotalAnnualisedCost + vic.TotalAnnualisedCost);
        breakdown.DeliveredEnergy.Should().Be(nsw.DeliveredEnergy + vic.DeliveredEnergy);
        nsw.LevelisedCostOfElectricity.Should().NotBe(vic.LevelisedCostOfElectricity,
            "each region must use its own delivered energy denominator");
        breakdown.SystemLevelisedCostOfElectricity.Should().Be(
            breakdown.TotalAnnualisedCost.Per(breakdown.DeliveredEnergy));
    }

    [Fact]
    public void Calculate_SingleRegionBreakdownEqualsSystemTotals()
    {
        PowerSystemCostBreakdown breakdown = PowerSystemCostCalculator.Calculate(
            MinimalScenario(Start.AddYears(1)),
            RunResult(DispatchOutcomeWithCurtailment()));

        RegionCostBreakdown region = breakdown.Regions.Should().ContainSingle().Subject;

        region.RegionId.Should().Be("NSW1");
        region.AnnualisedGenerationCost.Should().Be(breakdown.TotalAnnualisedGenerationCost);
        region.AnnualisedStorageCost.Should().Be(breakdown.TotalAnnualisedStorageCost);
        region.TotalAnnualisedCost.Should().Be(breakdown.TotalAnnualisedCost);
        region.DeliveredEnergy.Should().Be(breakdown.DeliveredEnergy);
        region.LevelisedCostOfGeneration.Should().Be(breakdown.SystemLevelisedCostOfGeneration);
        region.LevelisedCostOfStorage.Should().Be(breakdown.SystemLevelisedCostOfStorage);
        region.LevelisedCostOfElectricity.Should().Be(breakdown.SystemLevelisedCostOfElectricity);
    }

    [Fact]
    public void Calculate_RejectsScenarioThatIsNotExactlyOneYear()
    {
        Scenario scenario = MinimalScenario(Start.AddHours(1));

        var act = () => PowerSystemCostCalculator.Calculate(
            scenario,
            RunResult(DispatchOutcomeWithCurtailment()));

        act.Should().Throw<ArgumentException>().WithParameterName("scenario");
    }

    [Fact]
    public void Calculate_RejectsPowerSystemDerivedFromAnotherScenario()
    {
        Scenario scenario = MinimalScenario(
            Start.AddYears(1),
            new ScenarioId("different-scenario"));

        var act = () => PowerSystemCostCalculator.Calculate(
            scenario,
            RunResult(DispatchOutcomeWithCurtailment()));

        act.Should().Throw<ArgumentException>().WithParameterName("powerSystem");
    }

    [Fact]
    public void Calculate_RejectsZeroServedEnergy()
    {
        Scenario scenario = MinimalScenario(Start.AddYears(1));
        DispatchOutcome outcome = ZeroDispatchOutcome();

        var act = () => PowerSystemCostCalculator.Calculate(
            scenario,
            RunResult(outcome));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("deliveredEnergy")
            .WithMessage("*region 'NSW1'*");
    }

    [Fact]
    public void Calculate_RejectsRealisedStorageWithoutScenarioCostAssumptions()
    {
        Scenario scenario = MinimalScenario(Start.AddYears(1));

        var act = () => PowerSystemCostCalculator.Calculate(
            scenario,
            RunResult(
                DispatchOutcomeWithCurtailment(),
                Energy.FromMegawattHours(40),
                Power.FromMegawatts(10)));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("powerSystem")
            .WithMessage("Storage fleets lack scenario cost assumptions*");
    }

    [Fact]
    public void Calculate_ChargesInterconnectorCostAgainstItsLineDistanceAndCapacity()
    {
        Scenario scenario = TwoRegionScenario(Interconnectors());
        Money expectedTransmissionCost = OneLinkCost(capacityMw: 700);

        PowerSystemCostBreakdown breakdown = CalculateTwoRegion(scenario);

        breakdown.TotalAnnualisedTransmissionCost.Should().Be(
            expectedTransmissionCost,
            "cost scales with both the NSW1-VIC1 weather-site distance and the 700 MW directed capacity");
        breakdown.SystemLevelisedCostOfTransmission.Should().Be(
            expectedTransmissionCost.Per(breakdown.DeliveredEnergy));
        breakdown.TotalAnnualisedCost.Should().Be(
            breakdown.TotalAnnualisedGenerationCost
            + breakdown.TotalAnnualisedStorageCost
            + expectedTransmissionCost);
    }

    [Fact]
    public void Calculate_ChargesReciprocalInterconnectorsIndependently()
    {
        Scenario scenario = TwoRegionScenario(Interconnectors(includeReverse: true));
        Money expectedTransmissionCost = OneLinkCost(capacityMw: 700) + OneLinkCost(capacityMw: 400);

        PowerSystemCostBreakdown breakdown = CalculateTwoRegion(scenario);

        breakdown.TotalAnnualisedTransmissionCost.Should().Be(
            expectedTransmissionCost,
            "the NSW1 to VIC1 and VIC1 to NSW1 assets share a distance but each carry their own capacity and cost");
    }

    [Fact]
    public void Calculate_DoesNotRequireResourceProfilesForInterconnectorEndpoints()
    {
        // Transmission cost is charged against the interconnector's declared route length, not a
        // distance derived from the endpoints' weather sites, so a region missing a resource
        // profile entirely (NSW1, below) must not stop the system from being costed.
        Scenario scenario = TwoRegionScenario(Interconnectors());
        DispatchOutcome nswOutcome = DispatchOutcomeFor("NSW1", deliveredMegawattHours: 1);
        DispatchOutcome vicOutcome = DispatchOutcomeFor("VIC1", deliveredMegawattHours: 2);
        var powerSystem = new PowerSystem(
            new PowerSystemId("two-region-cost-system"),
            scenario.Id,
            [
                new Region(
                    "NSW1",
                    [new GeneratingFleet(GenerationTechnology.Gas, Power.FromMegawatts(10))],
                    nswOutcome.Demand),
                RegionFor("VIC1", vicOutcome.Demand),
            ],
            [Link()]);

        PowerSystemCostBreakdown breakdown = PowerSystemCostCalculator.Calculate(
            scenario,
            powerSystem,
            [nswOutcome, vicOutcome]);

        breakdown.TotalAnnualisedTransmissionCost.Should().Be(OneLinkCost(capacityMw: 700));
    }

    [Fact]
    public void Calculate_RegionalCostsExcludeTransmissionAndSoDoNotSumToTheSystemTotal()
    {
        Scenario scenario = TwoRegionScenario(Interconnectors());
        PowerSystemCostBreakdown breakdown = CalculateTwoRegion(scenario);

        Money regionalTotal = breakdown.Regions
            .Select(region => region.TotalAnnualisedCost)
            .Aggregate(Money.Zero, (left, right) => left + right);

        regionalTotal.Should().NotBe(
            breakdown.TotalAnnualisedCost,
            "an interconnector spans two regions and is deliberately not split between them");
        (breakdown.TotalAnnualisedCost - regionalTotal).Should().Be(
            breakdown.TotalAnnualisedTransmissionCost,
            "the gap between regional and system cost is exactly transmission");
    }

    [Fact]
    public void Calculate_WithoutInterconnectors_LeavesTransmissionCostAtZero()
    {
        PowerSystemCostBreakdown breakdown = CalculateTwoRegion(TwoRegionScenario());

        breakdown.TotalAnnualisedTransmissionCost.Should().Be(Money.Zero);
        breakdown.SystemLevelisedCostOfTransmission.Should().Be(default(EnergyPrice));
        breakdown.TotalAnnualisedCost.Should().Be(
            breakdown.Regions
                .Select(region => region.TotalAnnualisedCost)
                .Aggregate(Money.Zero, (left, right) => left + right),
            "with no links the regional and system totals must still agree");
    }

    [Fact]
    public void Calculate_ReportsNetImportedEnergySoTheRegionalDenominatorBiasIsVisible()
    {
        Scenario scenario = TwoRegionScenario(Interconnectors());
        DispatchOutcome exporter = TransferOutcome("NSW1", generation: 3, demand: 1, exports: 2);
        DispatchOutcome importer = TransferOutcome("VIC1", generation: 1, demand: 3, imports: 2);
        var powerSystem = new PowerSystem(
            new PowerSystemId("two-region-cost-system"),
            scenario.Id,
            [RegionFor("NSW1", exporter.Demand), RegionFor("VIC1", importer.Demand)],
            [Link()]);

        PowerSystemCostBreakdown breakdown = PowerSystemCostCalculator.Calculate(
            scenario,
            powerSystem,
            [exporter, importer]);

        breakdown.Regions.Single(region => region.RegionId == "NSW1").NetImportedEnergy
            .Should().Be(Energy.FromMegawattHours(-2));
        breakdown.Regions.Single(region => region.RegionId == "VIC1").NetImportedEnergy
            .Should().Be(
                Energy.FromMegawattHours(2),
                "a net importer serves load its own plant did not produce");
    }

    [Fact]
    public void Calculate_RejectsPowerSystemWhoseInterconnectorsDifferFromTheScenario()
    {
        Scenario scenario = TwoRegionScenario(Interconnectors());
        DispatchOutcome nswOutcome = DispatchOutcomeFor("NSW1", deliveredMegawattHours: 1);
        DispatchOutcome vicOutcome = DispatchOutcomeFor("VIC1", deliveredMegawattHours: 2);
        var powerSystem = new PowerSystem(
            new PowerSystemId("two-region-cost-system"),
            scenario.Id,
            [RegionFor("NSW1", nswOutcome.Demand), RegionFor("VIC1", vicOutcome.Demand)]);

        var act = () => PowerSystemCostCalculator.Calculate(
            scenario,
            powerSystem,
            [nswOutcome, vicOutcome]);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("powerSystem")
            .WithMessage("Scenario and power system must contain the same interconnectors*");
    }

    [Fact]
    public void Calculate_RejectsReciprocalSystemInterconnectorForTheWrongScenarioDirection()
    {
        Scenario scenario = TwoRegionScenario(Interconnectors());
        DispatchOutcome nswOutcome = DispatchOutcomeFor("NSW1", deliveredMegawattHours: 1);
        DispatchOutcome vicOutcome = DispatchOutcomeFor("VIC1", deliveredMegawattHours: 2);
        var powerSystem = new PowerSystem(
            new PowerSystemId("two-region-cost-system"),
            scenario.Id,
            [RegionFor("NSW1", nswOutcome.Demand), RegionFor("VIC1", vicOutcome.Demand)],
            [new Interconnector("VIC1", "NSW1", Power.FromMegawatts(700))]);

        var act = () => PowerSystemCostCalculator.Calculate(
            scenario,
            powerSystem,
            [nswOutcome, vicOutcome]);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("powerSystem")
            .WithMessage("Scenario and power system must contain the same interconnectors*");
    }

    private static PowerSystemCostBreakdown CalculateTwoRegion(Scenario scenario)
    {
        DispatchOutcome nswOutcome = DispatchOutcomeFor("NSW1", deliveredMegawattHours: 1);
        DispatchOutcome vicOutcome = DispatchOutcomeFor("VIC1", deliveredMegawattHours: 2);
        var powerSystem = new PowerSystem(
            new PowerSystemId("two-region-cost-system"),
            scenario.Id,
            [RegionFor("NSW1", nswOutcome.Demand), RegionFor("VIC1", vicOutcome.Demand)],
            scenario.Interconnectors.Count == 0
                ? null
                : scenario.Interconnectors.Select(interconnector => interconnector.ToInterconnector()).ToArray());

        return PowerSystemCostCalculator.Calculate(scenario, powerSystem, [nswOutcome, vicOutcome]);
    }

    private static Interconnector Link() =>
        new("NSW1", "VIC1", Power.FromMegawatts(700));

    private static IReadOnlyList<ScenarioInterconnector> Interconnectors(bool includeReverse = false) =>
        includeReverse
            ?
            [
                Interconnector("NSW1", "VIC1", 700),
                Interconnector("VIC1", "NSW1", 400),
            ]
            : [Interconnector("NSW1", "VIC1", 700)];

    private static ScenarioInterconnector Interconnector(
        string fromRegionId,
        string toRegionId,
        double capacityMw) =>
        new(
            fromRegionId,
            toRegionId,
            Power.FromMegawatts(capacityMw),
            NswLocation.DistanceTo(VicLocation),
            new TransmissionCostParameters(
                DistancePowerCost.FromAudPerKmPerMw(CapitalCostAudPerKmPerMw),
                AnnualDistancePowerCost.FromAudPerKmPerMwYear(FixedOperatingCostAudPerKmPerMwYear)),
            technicalLifeYears: InterconnectorTechnicalLifeYears);

    private const decimal CapitalCostAudPerKmPerMw = 1_000m;
    private const decimal FixedOperatingCostAudPerKmPerMwYear = 10m;
    private const uint InterconnectorTechnicalLifeYears = 50u;

    private static readonly GeoCoordinate NswLocation = GeoCoordinate.FromDegrees(-33.9, 151.2);
    private static readonly GeoCoordinate VicLocation = GeoCoordinate.FromDegrees(-37.8, 144.9);

    /// <summary>Annualised cost of one NSW1-VIC1 link at the shared test cost assumptions.</summary>
    private static Money OneLinkCost(double capacityMw)
    {
        Distance distance = NswLocation.DistanceTo(VicLocation);
        Power capacity = Power.FromMegawatts(capacityMw);
        return LevelisedCostCalculator.Annuitise(
                DistancePowerCost.FromAudPerKmPerMw(CapitalCostAudPerKmPerMw).For(distance, capacity),
                rate: 0m,
                years: InterconnectorTechnicalLifeYears)
            + AnnualDistancePowerCost.FromAudPerKmPerMwYear(FixedOperatingCostAudPerKmPerMwYear)
                .For(distance, capacity, years: 1);
    }

    private static DispatchOutcome TransferOutcome(
        string regionId,
        double generation,
        double demand,
        double imports = 0,
        double exports = 0)
    {
        FlowSeries generationFlow = AnnualFlow(generation);
        FlowSeries zero = AnnualFlow(0);
        return new DispatchOutcome(
            regionId,
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = generationFlow,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = zero,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = generationFlow,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = zero,
            },
            AnnualFlow(demand),
            zero,
            zero,
            zero,
            AnnualFlow(imports),
            AnnualFlow(exports));
    }

    private static StorageSizingRunResult RunResult(
        DispatchOutcome outcome,
        Energy? storageEnergy = null,
        Power? storagePower = null)
    {
        StorageFleet[] storageFleets = storageEnergy is null || storagePower is null
            ? []
            : [new StorageFleet(
                StorageTechnology.Battery,
                storageEnergy.Value,
                storagePower.Value,
                new StorageTechnologyProfile(15u, 0.87),
                Energy.Zero)];
        var system = new PowerSystem(
            new PowerSystemId("cost-breakdown-system"),
            new ScenarioId("cost-breakdown-scenario"),
            [new Region(
                "NSW1",
                [new GeneratingFleet(GenerationTechnology.Gas, Power.FromMegawatts(10))],
                outcome.Demand,
                storageFleets: storageFleets)]);
        var regionalResult = new RegionalSizingResult(
            outcome,
            new RegionalBatterySizing("NSW1", Energy.Zero, Power.Zero, wasChanged: false),
            meetsTarget: true,
            StorageSizingStatus.TargetMet,
            "Test dispatch is compliant.");

        return new StorageSizingRunResult(
            system,
            [regionalResult],
            [],
            dispatchPassCount: 1,
            StorageSizingStatus.TargetMet,
            "Test dispatch is compliant.");
    }

    private static DispatchOutcome DispatchOutcomeWithCurtailment()
    {
        FlowSeries generation = AnnualFlow(120, 70);
        return new DispatchOutcome(
            "NSW1",
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = generation,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = AnnualFlow(0, 0),
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = AnnualFlow(80, 70),
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = AnnualFlow(40, 0),
            },
            AnnualFlow(80, 100),
            AnnualFlow(0, 0),
            AnnualFlow(40, 0),
            AnnualFlow(0, 30),
            AnnualFlow(0, 0),
            AnnualFlow(0, 0));
    }

    private static DispatchOutcome ZeroDispatchOutcome()
    {
        FlowSeries zero = AnnualFlow(0);
        return new DispatchOutcome(
            "NSW1",
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = zero,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = zero,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = zero,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = zero,
            },
            zero,
            zero,
            zero,
            zero,
            zero,
            zero);
    }

    private static Scenario MinimalScenario(
        DateTimeOffset periodEnd,
        ScenarioId? scenarioId = null,
        IReadOnlyList<ScenarioStorageFleet>? storageFleets = null) =>
        new(
            scenarioId ?? new ScenarioId("cost-breakdown-scenario"),
            "Minimal cost scenario",
            Start,
            periodEnd,
            [new ScenarioRegion(
                "NSW1",
                [new ScenarioGeneratingFleet(
                    GenerationTechnology.Gas,
                    Power.FromMegawatts(10),
                    new GenerationCostParameters(
                        PowerCapacityCost.FromAudPerMwCapacity(0m),
                        AnnualPowerCapacityCost.FromAudPerMwYear(0m),
                        GenerationEnergyCost.FromAudPerMwhGenerated(0m),
                        FuelPrice.FromAudPerGjThermal(0m)),
                    new GenerationTechnologyProfile(
                        HeatRate.FromGigajoulesPerMegawattHour(0),
                        technicalLifeYears: 10u))],
                storageFleets)],
            new CostBasis(2026, realDiscountRate: 0m));

    private static Scenario TwoRegionScenario(
        IReadOnlyList<ScenarioInterconnector>? interconnectors = null) =>
        new(
            new ScenarioId("two-region-cost-scenario"),
            "Two-region cost scenario",
            Start,
            Start.AddYears(1),
            [
                ScenarioRegionFor("NSW1"),
                ScenarioRegionFor("VIC1"),
            ],
            new CostBasis(2026, realDiscountRate: 0m),
            interconnectors);

    private static ScenarioRegion ScenarioRegionFor(string regionId) =>
        new(
            regionId,
            [new ScenarioGeneratingFleet(
                GenerationTechnology.Gas,
                Power.FromMegawatts(10),
                new GenerationCostParameters(
                    PowerCapacityCost.FromAudPerMwCapacity(100m),
                    AnnualPowerCapacityCost.FromAudPerMwYear(0m),
                    GenerationEnergyCost.FromAudPerMwhGenerated(0m),
                    FuelPrice.FromAudPerGjThermal(0m)),
                new GenerationTechnologyProfile(
                    HeatRate.FromGigajoulesPerMegawattHour(0),
                    technicalLifeYears: 10u))]);

    private static Region RegionFor(string regionId, FlowSeries demand) =>
        new(
            regionId,
            [new GeneratingFleet(GenerationTechnology.Gas, Power.FromMegawatts(10))],
            demand,
            resourceProfile: ResourceProfileAt(LocationFor(regionId), demand));

    private static GeoCoordinate LocationFor(string regionId) =>
        string.Equals(regionId, "VIC1", StringComparison.OrdinalIgnoreCase) ? VicLocation : NswLocation;

    private static RegionalResourceProfile ResourceProfileAt(GeoCoordinate location, FlowSeries demand)
    {
        double[] zeroes = new double[demand.Length];
        return new RegionalResourceProfile(
            TraceSeries.GlobalHorizontalRadiation(demand.Start, demand.Resolution, zeroes),
            TraceSeries.DirectNormalRadiation(demand.Start, demand.Resolution, zeroes),
            TraceSeries.DiffuseHorizontalRadiation(demand.Start, demand.Resolution, zeroes),
            SolarZenithSeries.Calculate(
                demand.Start, demand.Resolution, demand.Length, location.Latitude, location.Longitude),
            TraceSeries.DryBulbTemperature(demand.Start, demand.Resolution, zeroes),
            TraceSeries.WindSpeed(demand.Start, demand.Resolution, zeroes, measurementHeightMetres: 10));
    }

    private static DispatchOutcome DispatchOutcomeFor(
        string regionId,
        double deliveredMegawattHours)
    {
        FlowSeries delivered = AnnualFlow(deliveredMegawattHours);
        FlowSeries zero = AnnualFlow(0);
        return new DispatchOutcome(
            regionId,
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = delivered,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = zero,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = delivered,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Gas] = zero,
            },
            delivered,
            zero,
            zero,
            zero,
            zero,
            zero);
    }

    private static FlowSeries AnnualFlow(params double[] initialMegawatts)
    {
        int hours = (int)(Start.AddYears(1) - Start).TotalHours;
        var values = new double[hours];
        initialMegawatts.CopyTo(values, 0);
        return new FlowSeries(Start, TimeSpan.FromHours(1), values);
    }
}