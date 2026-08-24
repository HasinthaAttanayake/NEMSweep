using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Simulation;

public sealed class SystemReliabilityAssessmentTests
{
    private static readonly DateTimeOffset NemStart =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Create_AllSystemAndRegionalMeasurementsWithinTarget_Passes()
    {
        SystemReliabilityAssessment assessment = SystemReliabilityAssessment.Create(
            SystemOutcome(
                Outcome("NSW1", demandMw: 100, unservedMw: 1),
                Outcome("VIC1", demandMw: 100, unservedMw: 2)),
            targetUsePercentage: 2);

        assessment.TargetUsePercentage.Should().Be(2);
        assessment.AchievedUsePercentage.Should().Be(1.5);
        assessment.WithinTarget.Should().BeTrue();
        assessment.Regions.Should().OnlyContain(region => region.WithinTarget);
    }

    [Fact]
    public void Create_SystemMeasurementExceedsTarget_Fails()
    {
        SystemReliabilityAssessment assessment = SystemReliabilityAssessment.Create(
            SystemOutcome(Outcome("NSW1", demandMw: 100, unservedMw: 6)),
            targetUsePercentage: 5);

        assessment.AchievedUsePercentage.Should().Be(6);
        assessment.WithinTarget.Should().BeFalse();
        assessment.Regions.Should().ContainSingle().Which.WithinTarget.Should().BeFalse();
    }

    [Fact]
    public void Create_SystemMeasurementPassesButOneRegionFails_Fails()
    {
        SystemReliabilityAssessment assessment = SystemReliabilityAssessment.Create(
            SystemOutcome(
                Outcome("NSW1", demandMw: 100, unservedMw: 10),
                Outcome("VIC1", demandMw: 900, unservedMw: 0)),
            targetUsePercentage: 5);

        assessment.AchievedUsePercentage.Should().Be(1);
        assessment.Regions.Should().Contain(region =>
            region.RegionId == "NSW1"
            && region.AchievedUsePercentage == 10
            && !region.WithinTarget);
        assessment.WithinTarget.Should().BeFalse();
    }

    [Fact]
    public void Create_UsesSystemDispatchReliabilityInsteadOfAverageRegionalUse()
    {
        SystemReliabilityAssessment assessment = SystemReliabilityAssessment.Create(
            SystemOutcome(
                Outcome("NSW1", demandMw: 100, unservedMw: 10),
                Outcome("VIC1", demandMw: 900, unservedMw: 0)),
            targetUsePercentage: 10);

        assessment.AchievedUsePercentage.Should().Be(1);
        assessment.Regions.Select(region => region.AchievedUsePercentage)
            .Average().Should().Be(5);
    }

    private static SystemDispatchOutcome SystemOutcome(params DispatchOutcome[] outcomes) =>
        SystemDispatchOutcome.Create(
            new PowerSystem(
                new PowerSystemId("test-system"),
                new ScenarioId("test-scenario"),
                outcomes.Select(outcome => new Region(
                    outcome.RegionId,
                    [new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(1_000))],
                    Hourly([outcome.Demand[0].Megawatts]))).ToArray()),
            outcomes);

    private static DispatchOutcome Outcome(string regionId, double demandMw, double unservedMw)
    {
        FlowSeries demand = Hourly([demandMw]);
        FlowSeries unserved = Hourly([unservedMw]);
        FlowSeries generation = Hourly([demandMw - unservedMw]);
        FlowSeries zero = Hourly([0]);
        var byTechnology = new Dictionary<GenerationTechnology, FlowSeries>
        {
            [GenerationTechnology.Coal] = generation,
        };

        return new DispatchOutcome(
            regionId,
            byTechnology,
            new Dictionary<GenerationTechnology, FlowSeries> { [GenerationTechnology.Coal] = zero },
            byTechnology,
            new Dictionary<GenerationTechnology, FlowSeries> { [GenerationTechnology.Coal] = zero },
            demand,
            unserved,
            zero,
            zero,
            zero,
            zero);
    }

    private static FlowSeries Hourly(double[] values) =>
        new(NemStart, TimeSpan.FromHours(1), values);
}