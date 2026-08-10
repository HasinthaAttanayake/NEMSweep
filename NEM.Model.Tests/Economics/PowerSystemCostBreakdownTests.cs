using AwesomeAssertions;
using NEM.Model.Economics;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
using NEM.Model.Units;

namespace NEM.Model.Tests.Economics;

public sealed class PowerSystemCostBreakdownTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void UsesLoadServedAndReconcilesAnnualGenerationCostComponents()
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
            .WithParameterName("deliveredEnergy");
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
                new StorageTechnologyProfile(15u, 0.87))];
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

    private static FlowSeries AnnualFlow(params double[] initialMegawatts)
    {
        int hours = (int)(Start.AddYears(1) - Start).TotalHours;
        var values = new double[hours];
        initialMegawatts.CopyTo(values, 0);
        return new FlowSeries(Start, TimeSpan.FromHours(1), values);
    }
}