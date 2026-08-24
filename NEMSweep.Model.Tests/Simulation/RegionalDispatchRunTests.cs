using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Simulation;

public sealed class RegionalDispatchRunTests
{
    private static readonly DateTimeOffset NemStart =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void ApplyImport_ClampsSubToleranceOverdelivery()
    {
        RegionalDispatchRun run = RunWithOneMegawattDeficit();

        run.ApplyImport(Power.FromMegawatts(1 + 5e-10));

        run.CurrentDeficit.Should().Be(Power.Zero);
    }

    [Fact]
    public void ApplyImport_RejectsMaterialOverdeliveryWithoutMutatingTheDeficit()
    {
        RegionalDispatchRun run = RunWithOneMegawattDeficit();

        Action act = () => run.ApplyImport(Power.FromMegawatts(1 + 2e-9));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sent more energy than it needed at index 0*");
        run.CurrentDeficit.Should().Be(Power.FromMegawatts(1));
    }

    private static RegionalDispatchRun RunWithOneMegawattDeficit()
    {
        var region = new Region(
            "NSW1",
            [new GeneratingFleet(GenerationTechnology.Coal, Power.Zero)],
            new FlowSeries(NemStart, TimeSpan.FromHours(1), [1]));
        var run = new RegionalDispatchRun(region, new NoStoragePolicy());
        run.BeginInterval(0);
        run.DispatchGeneration().Should().Be(Power.FromMegawatts(1));
        return run;
    }

    private sealed class NoStoragePolicy : IStoragePolicy
    {
        public StorageDecision Decide(DispatchContext context) => StorageDecision.None;
    }
}