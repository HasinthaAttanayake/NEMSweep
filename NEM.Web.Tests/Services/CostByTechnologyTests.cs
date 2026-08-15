using AwesomeAssertions;
using NEM.Contracts;
using NEM.Web.Services.Insights;

namespace NEM.Web.Tests.Services;

public sealed class CostByTechnologyTests
{
    [Fact]
    public void From_OrdersByAnnualCostSoTheLargestBillIsReadFirst()
    {
        CostByTechnology cost = CostByTechnology.From(Cost(), Mix());

        cost.Entries.Select(entry => entry.Technology).Should().Equal("Coal", "Wind", "Solar");
    }

    [Fact]
    public void From_StatesCostShareAgainstEnergyShareBecauseTheGapIsTheFinding()
    {
        CostByTechnology cost = CostByTechnology.From(Cost(), Mix());

        CostEntry coal = cost.Entries.Single(entry => entry.Technology == "Coal");
        // 600 of a 1,000 bill for 500 of 1,000 MWh: more of the money than of the energy.
        coal.CostShare.Should().Be(0.6);
        coal.EnergyShare.Should().Be(0.5);
        coal.CostToEnergyRatio.Should().Be(1.2);
    }

    [Fact]
    public void AudPerOwnMwh_PricesAFleetAgainstItsOwnOutputNotTheWholeSystems()
    {
        CostByTechnology cost = CostByTechnology.From(Cost(), Mix());

        // Solar costs 100 for the 200 MWh it delivered, which is not its 0.10/MWh contribution to
        // the system figure — that one is spread across every megawatt-hour served.
        CostEntry solar = cost.Entries.Single(entry => entry.Technology == "Solar");
        solar.AudPerOwnMwh.Should().Be(0.5m);
        solar.LevelisedContributionAudPerMwh.Should().Be(0.1m);
    }

    [Fact]
    public void AudPerOwnMwh_IsZeroRatherThanInfiniteForAFleetThatDeliveredNothing()
    {
        CostByTechnology cost = CostByTechnology.From(
            Cost() with
            {
                GenerationCostContributions =
                [
                    new DispatchGenerationCostContributionDTO("Gas", 50m, 0.05m),
                ],
            },
            EnergyMix.Empty);

        CostEntry gas = cost.Entries.Single();
        gas.AudPerOwnMwh.Should().Be(0);
        gas.EnergyShare.Should().Be(0);
        gas.CostToEnergyRatio.Should().Be(0);
    }

    [Fact]
    public void ReconcilesTo_FailsWhenTheContributionsDoNotSumToThePublishedGenerationCost()
    {
        CostByTechnology cost = CostByTechnology.From(Cost(), Mix());

        cost.ReconcilesTo(1000m).Should().BeTrue();
        cost.ReconcilesTo(1001m).Should().BeFalse();
    }

    [Fact]
    public void From_IsEmptyForACostThatPublishesNoContributions()
    {
        CostByTechnology.From(Cost() with { GenerationCostContributions = [] }, Mix())
            .Should().BeSameAs(CostByTechnology.Empty);
    }

    private static DispatchCostDTO Cost() => new(
        "calculated", 0, 0, 1000m, 1000m, 1m, 0, 0, 0,
        TransmissionCostStatus.NotModelled,
        0,
        [
            new DispatchGenerationCostContributionDTO("Solar", 100m, 0.1m),
            new DispatchGenerationCostContributionDTO("Coal", 600m, 0.6m),
            new DispatchGenerationCostContributionDTO("Wind", 300m, 0.3m),
        ]);

    private static EnergyMix Mix() => EnergyMix.FromTotals(new Dictionary<string, double>
    {
        ["Solar"] = 200,
        ["Coal"] = 500,
        ["Wind"] = 300,
    });
}
