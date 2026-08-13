using AwesomeAssertions;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.StorageSizing;
using NEM.Model.Units;

namespace NEM.Model.Tests.StorageSizing;

public sealed class StorageSizingServiceTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Size_PowerConstrainedCase_IntroducesKnownCompliantNearFrontierBattery()
    {
        PowerSystem system = SolarSystem(
            "NSW1",
            demandMw: [0, 30],
            directNormalRadiation: [2_000, 0]);

        StorageSizingRunResult result = StorageSizingService.Size(
            system,
            Options(maximumPowerMw: 100, maximumEnergyMwh: 400));

        result.Status.Should().Be(StorageSizingStatus.TargetMet);
        result.DispatchPassCount.Should().BeLessThan(30);
        RegionalSizingResult region = result.Regions.Should().ContainSingle().Subject;
        region.Reliability.UnservedEnergy.Should().Be(Energy.Zero);
        region.BatterySizing.PowerCapacity.Should().Be(Power.FromMegawatts(35));
        region.BatterySizing.EnergyCapacity.Should().Be(Energy.FromMegawattHours(140));
        region.BatterySizing.WasChanged.Should().BeTrue();
        result.PowerSystem.Regions.Single().StorageFleets.Single().TechnologyProfile
            .Should().Be(BatteryProfile());
        InstalledBatteryAssessment installed = result.InstalledBatteryAssessments
            .Should().ContainSingle().Subject;
        installed.MeetsTarget.Should().BeFalse();
        installed.BatteryCapacity.EnergyCapacity.Should().Be(Energy.Zero);
        installed.BatteryCapacity.PowerCapacity.Should().Be(Power.Zero);
        installed.BatteryCapacity.WasChanged.Should().BeFalse();
        system.Regions[0].StorageFleets.Should().BeEmpty();
        var mutableRegions = (IList<RegionalSizingResult>)result.Regions;
        var act = () => mutableRegions.Clear();
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Size_InstalledStorageMeetsTarget_RecordsAssessmentWithoutSolving()
    {
        Region region = CoalRegion("NSW1", generationMw: 50, demandMw: [50, 50])
            .WithBatteryStorage(
                Energy.FromMegawattHours(120),
                Power.FromMegawatts(30));
        var system = new PowerSystem(
            new PowerSystemId("installed-compliant-system"),
            new ScenarioId("test-scenario"),
            [region]);

        StorageSizingRunResult result = StorageSizingService.Size(
            system,
            Options(maximumPowerMw: 100, maximumEnergyMwh: 400));

        result.Status.Should().Be(StorageSizingStatus.TargetMet);
        result.DispatchPassCount.Should().Be(1);
        InstalledBatteryAssessment installed = result.InstalledBatteryAssessments
            .Should().ContainSingle().Subject;
        installed.MeetsTarget.Should().BeTrue();
        installed.Reliability.UnservedEnergy.Should().Be(Energy.Zero);
        installed.BatteryCapacity.EnergyCapacity.Should().Be(Energy.FromMegawattHours(120));
        installed.BatteryCapacity.PowerCapacity.Should().Be(Power.FromMegawatts(30));
        installed.BatteryCapacity.WasChanged.Should().BeFalse();
        result.Regions.Should().ContainSingle()
            .Which.BatterySizing.WasChanged.Should().BeFalse();
        result.PowerSystem.Should().BeSameAs(system);
    }

    [Fact]
    public void Size_ExistingBatteryIsStartingLowerBoundAndResultReportsTotalSizing()
    {
        Region region = SolarRegion("NSW1", [0, 30], [2_000, 0])
            .WithBatteryStorage(
                Energy.FromMegawattHours(128),
                Power.FromMegawatts(32));
        var system = new PowerSystem(
            new PowerSystemId("existing-battery-system"),
            new ScenarioId("test-scenario"),
            [region]);

        StorageSizingRunResult result = StorageSizingService.Size(
            system,
            Options(maximumPowerMw: 100, maximumEnergyMwh: 400));

        result.Status.Should().Be(StorageSizingStatus.TargetMet);
        InstalledBatteryAssessment installed = result.InstalledBatteryAssessments.Single();
        installed.MeetsTarget.Should().BeFalse();
        installed.BatteryCapacity.PowerCapacity.Should().Be(Power.FromMegawatts(32));
        installed.BatteryCapacity.EnergyCapacity.Should().Be(Energy.FromMegawattHours(128));
        installed.BatteryCapacity.WasChanged.Should().BeFalse();
        var sizing = result.Regions.Single().BatterySizing;
        sizing.PowerCapacity.Should().Be(Power.FromMegawatts(35));
        sizing.EnergyCapacity.Should().Be(Energy.FromMegawattHours(140));
        system.Regions[0].StorageFleets.Single().PowerCapacity
            .Should().Be(Power.FromMegawatts(32));
    }

    [Fact]
    public void Size_EnergyConstrainedCase_GrowsAndRefinesEnergyAtMinimumPower()
    {
        double[] demand = Enumerable.Repeat(0.0, 8)
            .Concat(Enumerable.Repeat(2.0, 65))
            .ToArray();
        double[] solar = Enumerable.Repeat(2_000.0, 8)
            .Concat(Enumerable.Repeat(0.0, 65))
            .ToArray();
        PowerSystem system = SolarSystem("NSW1", demand, solar);

        StorageSizingRunResult result = StorageSizingService.Size(
            system,
            Options(maximumPowerMw: 100, maximumEnergyMwh: 400));

        result.Status.Should().Be(StorageSizingStatus.TargetMet);
        var sizing = result.Regions.Single().BatterySizing;
        sizing.PowerCapacity.Should().Be(Power.FromMegawatts(30));
        sizing.EnergyCapacity.Should().Be(Energy.FromMegawattHours(130));
        result.Regions.Single().Reliability.UnservedEnergy.Should().Be(Energy.Zero);
    }

    [Fact]
    public void Size_MultipleRegions_ChangesOnlyFailingRegion()
    {
        Region compliant = CoalRegion("NSW1", generationMw: 50, demandMw: [50, 50]);
        Region failing = SolarRegion("QLD1", [0, 30], [2_000, 0]);
        var system = new PowerSystem(
            new PowerSystemId("multi-system"),
            new ScenarioId("test-scenario"),
            [compliant, failing]);

        StorageSizingRunResult result = StorageSizingService.Size(
            system,
            Options(maximumPowerMw: 100, maximumEnergyMwh: 400));

        result.Status.Should().Be(StorageSizingStatus.TargetMet);
        result.PowerSystem.Regions.Single(region => region.RegionId == "NSW1")
            .StorageFleets.Should().BeEmpty();
        result.PowerSystem.Regions.Single(region => region.RegionId == "QLD1")
            .StorageFleets.Should().ContainSingle(
                fleet => fleet.StorageTechnology == StorageTechnology.Battery);
        result.Regions.Should().OnlyContain(
            region => region.Reliability.UnservedEnergy == Energy.Zero);
    }

    [Fact]
    public void Size_SystemAvailableEnergyBelowDemand_ReturnsEnergyLimitedWithShortfall()
    {
        Region nsw = CoalRegion("NSW1", generationMw: 0, demandMw: [10, 10]);
        Region qld = CoalRegion("QLD1", generationMw: 5, demandMw: [10, 10]);
        var system = new PowerSystem(
            new PowerSystemId("insufficient-system"),
            new ScenarioId("test-scenario"),
            [nsw, qld]);

        StorageSizingRunResult result = StorageSizingService.Size(
            system,
            Options(maximumPowerMw: 60, maximumEnergyMwh: 240));

        result.Status.Should().Be(StorageSizingStatus.EnergyLimited);
        result.DispatchPassCount.Should().Be(1);
        result.Regions.Should().OnlyContain(region => region.Status == StorageSizingStatus.EnergyLimited);
        EnergyLimitedAssessment assessment = result.EnergyLimitedAssessment!;
        assessment.PowerSystemId.Should().Be(system.Id);
        assessment.AvailableEnergy.Should().Be(Energy.FromMegawattHours(10));
        assessment.DemandEnergy.Should().Be(Energy.FromMegawattHours(40));
        assessment.ShortfallEnergy.Should().Be(Energy.FromMegawattHours(30));
        assessment.BindingIntervalIndices.Should().Equal(0, 1);
        result.Regions.Should().OnlyContain(region =>
            region.BatterySizing.PowerCapacity <= Power.FromMegawatts(60)
            && region.BatterySizing.EnergyCapacity <= Energy.FromMegawattHours(240));
    }

    [Fact]
    public void Size_SystemAdequateEnergyButInsufficientBatteryPower_ReturnsBatteryCapacityLimit()
    {
        PowerSystem system = SolarSystem(
            "NSW1",
            demandMw: [0, 50],
            directNormalRadiation: [2_000, 0]);

        StorageSizingRunResult result = StorageSizingService.Size(
            system,
            Options(maximumPowerMw: 30, maximumEnergyMwh: 120));

        result.Status.Should().Be(StorageSizingStatus.BatteryCapacityLimitReached);
        result.EnergyLimitedAssessment.Should().BeNull();
        result.Regions.Single().Reliability.UnservedEnergy.Should().BeGreaterThan(Energy.Zero);
        result.TerminationEvidence.Should().Contain("30 MW / 120 MWh reached");
    }

    [Fact]
    public void Size_RegionalShortfallWithSystemAdequateEnergy_ReturnsStorageNoLongerImprovesReliability()
    {
        Region nsw = CoalRegion("NSW1", generationMw: 0, demandMw: [10, 10])
            .WithBatteryStorage(
                Energy.FromMegawattHours(120),
                Power.FromMegawatts(30));
        Region qld = CoalRegion("QLD1", generationMw: 20, demandMw: [10, 10]);
        var system = new PowerSystem(
            new PowerSystemId("system-adequate-energy"),
            new ScenarioId("test-scenario"),
            [nsw, qld]);

        StorageSizingRunResult result = StorageSizingService.Size(
            system,
            Options(maximumPowerMw: 60, maximumEnergyMwh: 240));

        result.Status.Should().Be(StorageSizingStatus.StorageNoLongerImprovesReliability);
        result.EnergyLimitedAssessment.Should().BeNull();
        result.DispatchPassCount.Should().Be(3);
        result.TerminationEvidence.Should().Contain("did not materially reduce unserved energy");
        result.Regions.Single(region => region.DispatchOutcome.RegionId == "NSW1")
            .Reliability.UnservedEnergy.Should().BeGreaterThan(Energy.Zero);
    }

    [Fact]
    public void Size_PassLimitReached_ReturnsLastDispatchEvidence()
    {
        PowerSystem system = SolarSystem(
            "NSW1",
            demandMw: [0, 30],
            directNormalRadiation: [2_000, 0]);

        StorageSizingRunResult result = StorageSizingService.Size(
            system,
            Options(maximumPowerMw: 100, maximumEnergyMwh: 400, maximumPasses: 1));

        result.Status.Should().Be(StorageSizingStatus.PassLimitReached);
        result.DispatchPassCount.Should().Be(1);
        result.Regions.Should().ContainSingle();
        result.Regions[0].Reliability.UnservedEnergy.Should().Be(Energy.FromMegawattHours(30));
    }

    [Fact]
    public void Options_RejectMaximumEnergyThatCannotSupportFourHourMaximumPower()
    {
        var act = () => Options(maximumPowerMw: 100, maximumEnergyMwh: 399);

        act.Should().Throw<ArgumentException>().WithParameterName("maximumEnergy");
    }

    [Fact]
    public void RegionalBatterySizing_RejectsNegativeCapacity()
    {
        var act = () => new RegionalBatterySizing(
            "NSW1",
            Energy.FromMegawattHours(-1),
            Power.Zero,
            wasChanged: false);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("energyCapacity");
    }

    [Fact]
    public void RegionalBatterySizing_RejectsUnpairedPowerAndEnergyCapacity()
    {
        var act = () => new RegionalBatterySizing(
            "NSW1",
            Energy.FromMegawattHours(120),
            Power.Zero,
            wasChanged: false);

        act.Should().Throw<ArgumentException>();
    }

    private static StorageSizingOptions Options(
        double maximumPowerMw,
        double maximumEnergyMwh,
        int maximumPasses = 256) =>
        new(
            Power.FromMegawatts(maximumPowerMw),
            Energy.FromMegawattHours(maximumEnergyMwh),
            targetUsePercentage: 0,
                maximumPasses: maximumPasses);

    private static StorageTechnologyProfile BatteryProfile() => new(15u, 0.87);

    private static PowerSystem SolarSystem(
        string regionId,
        double[] demandMw,
        double[] directNormalRadiation) =>
        new(
            new PowerSystemId("solar-system"),
            new ScenarioId("test-scenario"),
            [SolarRegion(regionId, demandMw, directNormalRadiation)]);

    private static Region SolarRegion(
        string regionId,
        double[] demandMw,
        double[] directNormalRadiation)
    {
        FlowSeries demand = Flow(demandMw);
        return new Region(
            regionId,
            [new GeneratingFleet(GenerationTechnology.Solar, Power.FromMegawatts(100))],
            demand,
            resourceProfile: StorageMonotonicityTests.Resources(demand, directNormalRadiation),
            storageTechnologyProfiles: BatteryProfiles());
    }

    private static Region CoalRegion(
        string regionId,
        double generationMw,
        double[] demandMw) =>
        new(
            regionId,
            [new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(generationMw))],
            Flow(demandMw),
            storageTechnologyProfiles: BatteryProfiles());

    private static IReadOnlyDictionary<StorageTechnology, StorageTechnologyProfile>
        BatteryProfiles() => new Dictionary<StorageTechnology, StorageTechnologyProfile>
        {
            [StorageTechnology.Battery] = BatteryProfile(),
        };

    private static FlowSeries Flow(double[] values) =>
        new(Start, TimeSpan.FromHours(1), values);
}
