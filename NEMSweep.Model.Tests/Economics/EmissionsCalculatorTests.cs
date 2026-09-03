using AwesomeAssertions;
using NEMSweep.Model.Economics;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Economics;

public sealed class EmissionsCalculatorTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    private const double CoalIntensity = 0.771;
    private const double GasIntensity = 0.364;

    [Fact]
    public void AccountsEachFleetsGrossGenerationAtItsScenarioIntensity()
    {
        Scenario scenario = TwoRegionScenario();
        DispatchOutcome nsw = OutcomeFor("NSW1", coalMw: 2, gasMw: 1);
        DispatchOutcome vic = OutcomeFor("VIC1", coalMw: 0, gasMw: 3);

        EmissionsSummary summary = EmissionsCalculator.Calculate(
            scenario,
            SystemFor(scenario, nsw, vic),
            [nsw, vic]);

        double hours = HoursInPeriod;
        summary.Regions.Single(region => region.RegionId == "NSW1")
            .TotalEmissions.TonnesCO2e
            .Should().BeApproximately(hours * ((2 * CoalIntensity) + (1 * GasIntensity)), 1e-6);
        summary.Regions.Single(region => region.RegionId == "VIC1")
            .TotalEmissions.TonnesCO2e
            .Should().BeApproximately(hours * 3 * GasIntensity, 1e-6);
    }

    [Fact]
    public void SystemContributionsSumToTheSystemTotalByTechnology()
    {
        Scenario scenario = TwoRegionScenario();
        DispatchOutcome nsw = OutcomeFor("NSW1", coalMw: 2, gasMw: 1);
        DispatchOutcome vic = OutcomeFor("VIC1", coalMw: 1, gasMw: 3);

        EmissionsSummary summary = EmissionsCalculator.Calculate(
            scenario,
            SystemFor(scenario, nsw, vic),
            [nsw, vic]);

        summary.GenerationEmissionsContributions
            .Sum(contribution => contribution.Emissions.TonnesCO2e)
            .Should().BeApproximately(summary.TotalEmissions.TonnesCO2e, 1e-9);
        summary.GenerationEmissionsContributions.Select(contribution => contribution.Technology)
            .Should().Equal(GenerationTechnology.Coal, GenerationTechnology.Gas);
        summary.TotalEmissions.Should().Be(
            summary.Regions[0].TotalEmissions + summary.Regions[1].TotalEmissions);
    }

    [Fact]
    public void CountsGenerationBookedToStorageChargingRatherThanDelivered()
    {
        Scenario scenario = TwoRegionScenario();
        DispatchOutcome delivered = OutcomeFor("NSW1", coalMw: 2, gasMw: 0);
        DispatchOutcome charging = OutcomeFor("NSW1", coalMw: 2, gasMw: 0, chargeMw: 1);
        DispatchOutcome vic = OutcomeFor("VIC1", coalMw: 0, gasMw: 1);

        EmissionsSummary deliveredSummary = EmissionsCalculator.Calculate(
            scenario, SystemFor(scenario, delivered, vic), [delivered, vic]);
        EmissionsSummary chargingSummary = EmissionsCalculator.Calculate(
            scenario, SystemFor(scenario, charging, vic), [charging, vic]);

        chargingSummary.Regions.Single(region => region.RegionId == "NSW1").TotalEmissions
            .Should().Be(
                deliveredSummary.Regions.Single(region => region.RegionId == "NSW1").TotalEmissions);
    }

    [Fact]
    public void ANonEmittingSystemAccountsNothing()
    {
        Scenario scenario = TwoRegionScenario(coalIntensity: 0, gasIntensity: 0);
        DispatchOutcome nsw = OutcomeFor("NSW1", coalMw: 2, gasMw: 1);
        DispatchOutcome vic = OutcomeFor("VIC1", coalMw: 1, gasMw: 1);

        EmissionsSummary summary = EmissionsCalculator.Calculate(
            scenario,
            SystemFor(scenario, nsw, vic),
            [nsw, vic]);

        summary.TotalEmissions.Should().Be(Emissions.Zero);
        summary.SystemEmissionsIntensity.Should().Be(ServedEmissionsIntensity.Zero);
        summary.GenerationEmissionsContributions.Should()
            .OnlyContain(contribution => contribution.Emissions == Emissions.Zero);
    }

    [Fact]
    public void SystemIntensityDividesSystemEmissionsBySystemEnergyServed()
    {
        Scenario scenario = TwoRegionScenario();
        DispatchOutcome nsw = OutcomeFor("NSW1", coalMw: 2, gasMw: 1);
        DispatchOutcome vic = OutcomeFor("VIC1", coalMw: 1, gasMw: 3);

        EmissionsSummary summary = EmissionsCalculator.Calculate(
            scenario,
            SystemFor(scenario, nsw, vic),
            [nsw, vic]);

        summary.EnergyServed.MegawattHours.Should().BeApproximately(HoursInPeriod * 7, 1e-6);
        summary.SystemEmissionsIntensity.TonnesCO2ePerMwhServed
            .Should().BeApproximately(
                summary.TotalEmissions.TonnesCO2e
                    / summary.EnergyServed.MegawattHours,
                1e-12);
    }

    [Fact]
    public void RejectsOutcomesThatDoNotCoverEverySystemRegion()
    {
        Scenario scenario = TwoRegionScenario();
        DispatchOutcome nsw = OutcomeFor("NSW1", coalMw: 1, gasMw: 1);
        DispatchOutcome vic = OutcomeFor("VIC1", coalMw: 1, gasMw: 1);

        var act = () => EmissionsCalculator.Calculate(
            scenario,
            SystemFor(scenario, nsw, vic),
            [nsw]);

        act.Should().Throw<ArgumentException>().WithParameterName("dispatchOutcomes");
    }

    [Fact]
    public void RejectsAScenarioThatDoesNotCoverExactlyOneYear()
    {
        Scenario scenario = TwoRegionScenario(periodEnd: Start.AddYears(2));
        DispatchOutcome nsw = OutcomeFor("NSW1", coalMw: 1, gasMw: 1);
        DispatchOutcome vic = OutcomeFor("VIC1", coalMw: 1, gasMw: 1);

        var act = () => EmissionsCalculator.Calculate(
            scenario,
            SystemFor(scenario, nsw, vic),
            [nsw, vic]);

        act.Should().Throw<ArgumentException>().WithParameterName("scenario");
    }

    private static int HoursInPeriod => (int)(Start.AddYears(1) - Start).TotalHours;

    private static Scenario TwoRegionScenario(
        double coalIntensity = CoalIntensity,
        double gasIntensity = GasIntensity,
        DateTimeOffset? periodEnd = null) =>
        new(
            new ScenarioId("emissions-scenario"),
            "Emissions scenario",
            Start,
            periodEnd ?? Start.AddYears(1),
            [
                ScenarioRegionFor("NSW1", coalIntensity, gasIntensity),
                ScenarioRegionFor("VIC1", coalIntensity, gasIntensity),
            ],
            new CostBasis(2026, realDiscountRate: 0m));

    private static ScenarioRegion ScenarioRegionFor(
        string regionId,
        double coalIntensity,
        double gasIntensity) =>
        new(
            regionId,
            [
                ScenarioFleetFor(GenerationTechnology.Coal, coalIntensity),
                ScenarioFleetFor(GenerationTechnology.Gas, gasIntensity),
            ]);

    private static ScenarioGeneratingFleet ScenarioFleetFor(
        GenerationTechnology technology,
        double intensity) =>
        new(
            technology,
            Power.FromMegawatts(10),
            new GenerationCostParameters(
                PowerCapacityCost.FromAudPerMwCapacity(0m),
                AnnualPowerCapacityCost.FromAudPerMwYear(0m),
                GenerationEnergyCost.FromAudPerMwhGenerated(0m),
                FuelPrice.FromAudPerGjThermal(0m)),
            new GenerationTechnologyProfile(
                HeatRate.FromGigajoulesPerMegawattHour(0),
                technicalLifeYears: 10u,
                GenerationEmissionsIntensity.FromTonnesCO2ePerMwhGenerated(intensity)));

    private static PowerSystem SystemFor(
        Scenario scenario,
        params DispatchOutcome[] outcomes) =>
        new(
            new PowerSystemId("emissions-system"),
            scenario.Id,
            outcomes.Select(outcome => new Region(
                outcome.RegionId,
                [
                    new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(10)),
                    new GeneratingFleet(GenerationTechnology.Gas, Power.FromMegawatts(10)),
                ],
                outcome.Demand)).ToArray());

    /// <summary>
    /// A flat outcome that generates the given power all year. When <paramref name="chargeMw"/> is
    /// positive, that much generation is booked to storage charging instead of load, so generation
    /// is unchanged while served energy falls.
    /// </summary>
    private static DispatchOutcome OutcomeFor(
        string regionId,
        double coalMw,
        double gasMw,
        double chargeMw = 0)
    {
        double deliveredCoal = Math.Max(0, coalMw - chargeMw);
        double chargedCoal = coalMw - deliveredCoal;
        return new DispatchOutcome(
            regionId,
            Fleets(coalMw, gasMw),
            Fleets(0, 0),
            Fleets(deliveredCoal, gasMw),
            Fleets(chargedCoal, 0),
            Flat(deliveredCoal + gasMw),
            Flat(0),
            Flat(chargedCoal),
            Flat(0),
            Flat(0),
            Flat(0));
    }

    private static Dictionary<GenerationTechnology, FlowSeries> Fleets(double coalMw, double gasMw) =>
        new()
        {
            [GenerationTechnology.Coal] = Flat(coalMw),
            [GenerationTechnology.Gas] = Flat(gasMw),
        };

    private static FlowSeries Flat(double megawatts)
    {
        var values = new double[HoursInPeriod];
        Array.Fill(values, megawatts);
        return new FlowSeries(Start, TimeSpan.FromHours(1), values);
    }
}
