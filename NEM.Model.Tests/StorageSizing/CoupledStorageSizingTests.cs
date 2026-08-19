using AwesomeAssertions;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
using NEM.Model.Units;

namespace NEM.Model.Tests.StorageSizing;

/// <summary>
/// Storage sizing across regions coupled by an interconnector. Sizing already
/// re-dispatches the whole system on every pass, so coupling is inherited from
/// dispatch; what these tests pin is that it terminates, that it reports which bound
/// it stopped on, and how monotonicity behaves once transfers redistribute surplus.
/// </summary>
public sealed class CoupledStorageSizingTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Size_CoupledRegions_RetainsInterconnectorsAndFinalFlowEvidence()
    {
        PowerSystem system = TwoRegionSystem(
            nswDemand: [100, 100],
            vicDemand: [100, 100],
            nswCapacityMw: 60,
            vicCapacityMw: 60,
            linkMw: 40);

        StorageSizingRunResult result = StorageSizingService.Size(system, Options());

        result.PowerSystem.Interconnectors.Should().ContainSingle(
            "sizing rebuilds regions on every candidate, and the link must survive each rebuild");
        result.PowerSystem.Interconnectors[0].Capacity.Should().Be(Power.FromMegawatts(40));
        InterconnectorFlow flow = result.InterconnectorFlows.Should().ContainSingle(
            "the final sizing result must retain solver evidence for its surviving link").Subject;
        flow.Interconnector.Should().Be(result.PowerSystem.Interconnectors[0]);
        flow.Flow.Length.Should().Be(2);
        flow.Losses.Length.Should().Be(2);
    }

    [Fact]
    public void Size_SurplusReachableThroughTheLink_NeedsLessStorageThanIndependentRegions()
    {
        // NSW1 has slack hours in which a battery could charge, so sizing is a genuine
        // option for it; VIC1 has ample spare capacity throughout.
        double[] tight = [0, 0, 100, 100];
        double[] ample = [10, 10, 10, 10];

        StorageSizingRunResult coupled = StorageSizingService.Size(
            TwoRegionSystem(
                tight,
                ample,
                nswCapacityMw: 60,
                vicCapacityMw: 200,
                linkMw: 100,
                linkFromRegionId: "VIC1",
                linkToRegionId: "NSW1"),
            Options());
        StorageSizingRunResult independent = StorageSizingService.Size(
            TwoRegionSystem(tight, ample, nswCapacityMw: 60, vicCapacityMw: 200, linkMw: 0),
            Options());

        Energy coupledStorage = TotalBatteryEnergy(coupled.PowerSystem);
        Energy independentStorage = TotalBatteryEnergy(independent.PowerSystem);

        coupled.Status.Should().Be(StorageSizingStatus.TargetMet);
        independent.Status.Should().Be(
            StorageSizingStatus.TargetMet,
            "both runs must reach compliance for the comparison to be like for like");
        coupledStorage.MegawattHours.Should().BeLessThan(
            independentStorage.MegawattHours,
            "imports carry part of the adequacy burden that storage would otherwise carry alone; "
            + "interconnection is worth {0} MWh of storage here",
            independentStorage.MegawattHours - coupledStorage.MegawattHours);
    }

    [Fact]
    public void Size_CoupledRegionsThatCannotComply_TerminatesAndReportsTheBindingBound()
    {
        PowerSystem system = TwoRegionSystem(
            nswDemand: [500, 500],
            vicDemand: [500, 500],
            nswCapacityMw: 1,
            vicCapacityMw: 1,
            linkMw: 10);

        StorageSizingRunResult result = StorageSizingService.Size(system, Options(maximumPasses: 5));

        result.Status.Should().NotBe(
            StorageSizingStatus.TargetMet,
            "no amount of storage can serve demand that generation cannot supply");
        result.Status.Should().BeOneOf(
            StorageSizingStatus.EnergyLimited,
            StorageSizingStatus.PassLimitReached,
            StorageSizingStatus.BatteryCapacityLimitReached);
        result.TerminationEvidence.Should().NotBeEmpty("the run must say which bound it stopped on");
        result.DispatchPassCount.Should().BeLessThanOrEqualTo(5);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(41)]
    [InlineData(2026)]
    public void Dispatch_AddingStorageToOneRegion_NeverIncreasesUnservedEnergyInTheOther(int seed)
    {
        var random = new Random(seed);

        for (int sample = 0; sample < 25; sample++)
        {
            double[] nswDemand = Enumerable.Range(0, 12)
                .Select(_ => (double)random.Next(0, 120)).ToArray();
            double[] vicDemand = Enumerable.Range(0, 12)
                .Select(_ => (double)random.Next(0, 120)).ToArray();

            IReadOnlyList<DispatchOutcome> without = Dispatcher.Dispatch(TwoRegionSystem(
                nswDemand,
                vicDemand,
                nswCapacityMw: 70,
                vicCapacityMw: 70,
                linkMw: 40));
            IReadOnlyList<DispatchOutcome> with = Dispatcher.Dispatch(TwoRegionSystem(
                nswDemand,
                vicDemand,
                nswCapacityMw: 70,
                vicCapacityMw: 70,
                linkMw: 40,
                nswBattery: (EnergyMwh: 200, PowerMw: 50)));

            with[1].Reliability.UnservedEnergy.MegawattHours.Should().BeLessThanOrEqualTo(
                without[1].Reliability.UnservedEnergy.MegawattHours + 1e-9,
                "storage added to NSW1 must not make VIC1 worse: transfer is settled before "
                + "storage charges, so a battery cannot outbid a neighbour's unserved load");
        }
    }

    private static Energy TotalBatteryEnergy(PowerSystem system) =>
        system.Regions
            .SelectMany(region => region.StorageFleets)
            .Where(fleet => fleet.StorageTechnology == StorageTechnology.Battery)
            .Aggregate(Energy.Zero, (total, fleet) => total + fleet.StorageCapacity);

    private static StorageSizingOptions Options(int maximumPasses = 64) =>
        new(
            Power.FromMegawatts(500),
            Energy.FromMegawattHours(4_000),
            maximumPasses: maximumPasses);

    private static PowerSystem TwoRegionSystem(
        double[] nswDemand,
        double[] vicDemand,
        double nswCapacityMw,
        double vicCapacityMw,
        double linkMw,
        (double EnergyMwh, double PowerMw)? nswBattery = null,
        string linkFromRegionId = "NSW1",
        string linkToRegionId = "VIC1") =>
        new(
            new PowerSystemId("coupled-sizing-system"),
            new ScenarioId("coupled-sizing-scenario"),
            [
                RegionFor("NSW1", nswDemand, nswCapacityMw, nswBattery),
                RegionFor("VIC1", vicDemand, vicCapacityMw, null),
            ],
            linkMw <= 0
                ? []
                : [new Interconnector(
                    linkFromRegionId,
                    linkToRegionId,
                    Power.FromMegawatts(linkMw))]);

    private static Region RegionFor(
        string regionId,
        double[] demandMw,
        double capacityMw,
        (double EnergyMwh, double PowerMw)? battery) =>
        new(
            regionId,
            [new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(capacityMw))],
            new FlowSeries(Start, TimeSpan.FromHours(1), demandMw),
            storageFleets: battery is null
                ? []
                : [new StorageFleet(
                    StorageTechnology.Battery,
                    Energy.FromMegawattHours(battery.Value.EnergyMwh),
                    Power.FromMegawatts(battery.Value.PowerMw),
                    new StorageTechnologyProfile(15u, 0.87),
                    Energy.Zero)],
            storageTechnologyProfiles: new Dictionary<StorageTechnology, StorageTechnologyProfile>
            {
                [StorageTechnology.Battery] = new StorageTechnologyProfile(15u, 0.87),
            });
}
