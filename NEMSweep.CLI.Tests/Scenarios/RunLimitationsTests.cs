using AwesomeAssertions;
using NEMSweep.CLI.Scenarios;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Tests.Scenarios;

public sealed class RunLimitationsTests
{
    [Fact]
    public void Create_AlwaysCarriesTheEngineLimitationsAndTheReference()
    {
        RunLimitationsDTO limitations = CreateSized();

        limitations.Engine.Should().NotBeEmpty();
        limitations.Engine.Should().Contain(statement => statement.Contains("no foresight"));
        limitations.Engine.Should().Contain(statement => statement.Contains("Only Battery capacity is sized"));
        limitations.Engine.Should().Contain(statement => statement.Contains("not a retail price"));
        limitations.Reference.Should().Be("https://docs.nemsweep.com/assumptions/limitations.html");
    }

    /// <summary>
    /// The engine block travels with every result, so it must not acquire anything about the data a
    /// particular run consumed. A statement about a dataset belongs in the run-specific block, or
    /// in the starter that ships that dataset, and nowhere else.
    /// </summary>
    [Fact]
    public void Create_EngineLimitations_SayNothingAboutAnyParticularDataset()
    {
        string[] datasetWords = ["NEM", "AEMO", "Australia", "NSW", "TMYx", "FY2026", "0.002"];

        string[] engine = CreateSized().Engine;

        foreach (string word in datasetWords)
        {
            engine.Should().NotContain(
                statement => statement.Contains(word, StringComparison.OrdinalIgnoreCase),
                $"'{word}' describes one dataset, not the engine");
        }
    }

    [Fact]
    public void Create_StatesThePeriodTheTargetWasTestedOver()
    {
        RunLimitationsDTO limitations = CreateSized();

        limitations.ThisRun.Should().Contain(statement =>
            statement.Contains("2025-07-01")
            && statement.Contains("2026-07-01")
            && statement.Contains("no other period"));
    }

    [Fact]
    public void Create_TargetMet_DoesNotClaimTheTargetWasMissed()
    {
        RunLimitationsDTO limitations = CreateSized();

        limitations.ThisRun.Should().NotContain(statement => statement.Contains("The target was not met"));
    }

    [Fact]
    public void Create_TargetMissed_SaysSoInTheRunSpecificBlock()
    {
        RunLimitationsDTO limitations = CreateSized(reliability: new ReliabilityBasisDTO(0.002, 0.024, false, null));

        limitations.ThisRun.Should().Contain(statement =>
            statement.Contains("The target was not met") && statement.Contains("0.024"));
    }

    [Fact]
    public void Create_FixedFleet_SaysNothingWasSized()
    {
        RunLimitationsDTO limitations = Create(sizing: null);

        limitations.ThisRun.Should().Contain(statement => statement.Contains("fixed-fleet"));
    }

    /// <summary>
    /// The distinction the search itself draws, and the one a reader most often loses: only the
    /// energy-limited outcome rules out a larger battery.
    /// </summary>
    [Theory]
    [InlineData(StorageSizingOutcome.EnergyLimited, "No battery size could have met the target")]
    [InlineData(StorageSizingOutcome.BatteryCapacityLimitReached, "has not established that no battery would meet the target")]
    [InlineData(StorageSizingOutcome.PassLimitReached, "has not established that no battery would meet the target")]
    [InlineData(StorageSizingOutcome.StorageNoLongerImprovesReliability, "does not establish that no battery would work")]
    public void Create_SizingOutcome_SaysWhetherALargerBatteryWasRuledOut(
        StorageSizingOutcome outcome,
        string expected)
    {
        RunLimitationsDTO limitations = Create(sizing: Sizing() with { Outcome = outcome });

        limitations.ThisRun.Should().Contain(statement => statement.Contains(expected));
    }

    [Fact]
    public void Create_CapacityLimit_LabelsSystemBoundsAsSummedAcrossRegions()
    {
        RunLimitationsDTO limitations = Create(sizing: Sizing() with
        {
            Outcome = StorageSizingOutcome.BatteryCapacityLimitReached,
            MaximumEnergyMwh = 800,
            MaximumPowerMw = 200,
        });

        limitations.ThisRun.Should().Contain(statement =>
            statement.Contains("800.0 MWh and 200.0 MW summed across regions"));
        limitations.ThisRun.Should().NotContain(statement => statement.Contains("per region"));
    }

    [Fact]
    public void Create_Resized_ReportsWhatWasAddedWithoutCallingItOptimal()
    {
        RunLimitationsDTO limitations = CreateSized();

        limitations.ThisRun.Should().Contain(statement =>
            statement.Contains("grew battery energy by 250.0 MWh")
            && statement.Contains("not a least-cost"));
    }

    [Fact]
    public void Create_TypicalMeteorologicalYearWeather_WarnsThatTailEventsAreAbsent()
    {
        RunLimitationsDTO limitations = CreateSized();

        limitations.ThisRun.Should().Contain(statement =>
            statement.Contains("typical meteorological year") && statement.Contains("understated"));
    }

    [Fact]
    public void Create_NoWeatherBasisSupplied_MakesNoClaimAboutTheWeather()
    {
        RunLimitationsDTO limitations = CreateSized(weatherBases: []);

        limitations.ThisRun.Should().NotContain(statement => statement.Contains("typical meteorological year"));
    }

    [Fact]
    public void Create_StorageTheSearchCannotGrow_PointsAtTheBatteryOnlyFields()
    {
        RunLimitationsDTO limitations = CreateSized(storageCapacity: Breakdown() with
        {
            PumpedHydroEnergyMwh = 15_680,
        });

        limitations.ThisRun.Should().Contain(statement =>
            statement.Contains("15,680.0 MWh") && statement.Contains("storageCapacity"));
    }

    [Fact]
    public void Create_NoStorageBeyondBattery_DoesNotRaiseTheDistinction()
    {
        RunLimitationsDTO limitations = CreateSized();

        limitations.ThisRun.Should().NotContain(statement => statement.Contains("cannot grow"));
    }

    /// <summary>
    /// A sized run that meets its target, against typical-meteorological-year weather, with no
    /// storage the search cannot grow. Each test overrides the one thing it is about.
    /// </summary>
    /// <param name="sizing">
    /// Sizing evidence. Defaults to a resized outcome; pass <see langword="null"/> together with
    /// <c>fixedFleet: true</c> for the fixed-fleet case, since null alone cannot be told from an
    /// omitted argument.
    /// </param>
    private static RunLimitationsDTO Create(
        ReliabilityBasisDTO? reliability = null,
        StorageSizingOutcomeDTO? sizing = null,
        StorageCapacityBreakdownDTO? storageCapacity = null,
        IEnumerable<WeatherBasisDTO>? weatherBases = null) =>
        RunLimitations.Create(
            reliability ?? new ReliabilityBasisDTO(0.002, 0.001, true, "A standard"),
            sizing,
            storageCapacity ?? Breakdown(),
            weatherBases ?? [WeatherBasis()],
            new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10)),
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10)));

    /// <summary>The default sized run, for the tests that are not about the sizing outcome.</summary>
    private static RunLimitationsDTO CreateSized(
        ReliabilityBasisDTO? reliability = null,
        StorageCapacityBreakdownDTO? storageCapacity = null,
        IEnumerable<WeatherBasisDTO>? weatherBases = null) =>
        Create(reliability, Sizing(), storageCapacity, weatherBases);

    private static StorageSizingOutcomeDTO Sizing() =>
        new(
            StorageSizingOutcome.Resized,
            InitialEnergyMwh: 150,
            InitialPowerMw: 40,
            FinalEnergyMwh: 400,
            FinalPowerMw: 100,
            MaximumEnergyMwh: 100_000,
            MaximumPowerMw: 10_000,
            PassesUsed: 7);

    private static StorageCapacityBreakdownDTO Breakdown() =>
        new(
            DeclaredBatteryEnergyMwh: 150,
            DeclaredBatteryPowerMw: 40,
            AddedBatteryEnergyMwh: 250,
            AddedBatteryPowerMw: 60,
            TotalBatteryEnergyMwh: 400,
            TotalBatteryPowerMw: 100,
            PumpedHydroEnergyMwh: 0,
            PumpedHydroPowerMw: 0,
            OtherStorageEnergyMwh: 0,
            OtherStoragePowerMw: 0);

    private static WeatherBasisDTO WeatherBasis() =>
        new(
            WeatherBasisKind.TypicalMeteorologicalYear,
            new WeatherSiteDTO("solar.epw", "A site"),
            new WeatherSiteDTO("wind.epw", "A site"),
            "A typical meteorological year.");
}
