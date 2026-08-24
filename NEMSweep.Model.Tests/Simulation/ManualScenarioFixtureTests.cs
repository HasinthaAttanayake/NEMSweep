using System.Text.Json;
using AwesomeAssertions;
using NEMSweep.Model.Generation.Wind;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;
using NEMSweep.Model.Weather;

namespace NEMSweep.Model.Tests.Simulation;

public sealed class ManualScenarioFixtureTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    public static TheoryData<string> FixtureFiles => new(
        "01-thermal-deficit.json",
        "02-wind-curtailment.json",
        "03-battery-shift.json",
        "04-battery-energy-limit.json");

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Dispatch_MatchesManuallyCalculatedFixture(string fixtureFile)
    {
        ManualScenarioFixture fixture = ReadFixture(fixtureFile);
        fixture.Arithmetic.Should().NotBeEmpty(
            "each fixture must retain legible hand arithmetic for the writeup");
        fixture.Expected.AllSeries.Should().OnlyContain(
            values => values.Length == fixture.DemandMw.Length);

        FlowSeries demand = Flow(fixture.DemandMw);
        GenerationTechnology technology = Enum.Parse<GenerationTechnology>(
            fixture.Fleet.Technology,
            ignoreCase: true);
        var fleet = new GeneratingFleet(
            technology,
            Power.FromMegawatts(fixture.Fleet.CapacityMw));
        StorageFleet[] storageFleets = fixture.Battery is null
            ? []
            :
            [
                new StorageFleet(
                    StorageTechnology.Battery,
                    Energy.FromMegawattHours(fixture.Battery.EnergyCapacityMwh),
                    Power.FromMegawatts(fixture.Battery.PowerCapacityMw),
                    new StorageTechnologyProfile(15u, 0.87),
                    Energy.Zero),
            ];
        var region = new Region(
            "NSW1",
            [fleet],
            demand,
            resourceProfile: technology == GenerationTechnology.Wind
                ? WindResources(demand, fixture.Fleet)
                : null,
            storageFleets: storageFleets);
        var system = new PowerSystem(
            new PowerSystemId($"manual-{fixture.Id}"),
            new ScenarioId("manual-scenarios"),
            [region]);

        DispatchOutcome outcome = Dispatcher.Dispatch(system).Single();

        AssertSeries(outcome.PerFleetGeneration[technology], fixture.Expected.GenerationMw);
        AssertSeries(outcome.Charge, fixture.Expected.ChargeMw);
        AssertSeries(outcome.Discharge, fixture.Expected.DischargeMw);
        AssertSeries(outcome.Curtailment, fixture.Expected.CurtailmentMw);
        AssertSeries(outcome.Unserved, fixture.Expected.UnservedMw);
        if (fixture.Battery is not null)
        {
            AssertStockSeries(
                outcome.StateOfChargeByTechnology[StorageTechnology.Battery],
                fixture.Expected.StateOfChargeMwh);
        }

        for (int hour = 0; hour < demand.Length; hour++)
        {
            double inputs = outcome.PerFleetGeneration.Values.Sum(series => series[hour].Megawatts)
                + outcome.Discharge[hour].Megawatts
                + outcome.Imports[hour].Megawatts
                + outcome.Unserved[hour].Megawatts;
            double outputs = outcome.Demand[hour].Megawatts
                + outcome.Charge[hour].Megawatts
                + outcome.Exports[hour].Megawatts
                + outcome.Curtailment[hour].Megawatts;

            inputs.Should().BeApproximately(outputs, 1e-9, fixture.Title);
        }
    }

    private static ManualScenarioFixture ReadFixture(string fixtureFile)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Simulation",
            "Fixtures",
            fixtureFile);
        return JsonSerializer.Deserialize<ManualScenarioFixture>(
            File.ReadAllBytes(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Fixture {fixtureFile} is empty.");
    }

    private static RegionalResourceProfile WindResources(
        FlowSeries timeline,
        FleetFixture fleet)
    {
        ArgumentNullException.ThrowIfNull(fleet.AvailableMw);
        double[] windSpeed = fleet.AvailableMw.Select(availableMw =>
        {
            if (availableMw == 0)
            {
                return 0;
            }

            if (availableMw == fleet.CapacityMw)
            {
                return WindPowerCurve.RatedWindSpeedMetresPerSecond;
            }

            throw new InvalidOperationException(
                "Manual wind fixtures support only zero or nameplate availability.");
        }).ToArray();
        var zeros = new double[timeline.Length];
        return new RegionalResourceProfile(
            TraceSeries.GlobalHorizontalRadiation(Start, timeline.Resolution, zeros),
            TraceSeries.DirectNormalRadiation(Start, timeline.Resolution, zeros),
            TraceSeries.DiffuseHorizontalRadiation(Start, timeline.Resolution, zeros),
            SolarZenithSeries.Calculate(
                Start,
                timeline.Resolution,
                timeline.Length,
                latitude: -33.8688,
                longitude: 151.2093),
            TraceSeries.DryBulbTemperature(Start, timeline.Resolution, zeros),
            TraceSeries.WindSpeed(
                Start,
                timeline.Resolution,
                windSpeed,
                WindPowerCurve.DefaultHubHeightMetres));
    }

    private static FlowSeries Flow(double[] values) =>
        new(Start, TimeSpan.FromHours(1), values);

    private static void AssertSeries(FlowSeries actual, double[] expected)
    {
        for (int index = 0; index < expected.Length; index++)
        {
            actual[index].Megawatts.Should().BeApproximately(expected[index], 1e-9);
        }
    }

    private static void AssertStockSeries(StockSeries actual, double[] expected)
    {
        for (int index = 0; index < expected.Length; index++)
        {
            actual[index].MegawattHours.Should().BeApproximately(expected[index], 1e-9);
        }
    }

    private sealed record ManualScenarioFixture(
        string Id,
        string Title,
        string[] Arithmetic,
        double[] DemandMw,
        FleetFixture Fleet,
        BatteryFixture? Battery,
        ExpectedFixture Expected);

    private sealed record FleetFixture(
        string Technology,
        double CapacityMw,
        double[]? AvailableMw = null);

    private sealed record BatteryFixture(
        double EnergyCapacityMwh,
        double PowerCapacityMw);

    private sealed record ExpectedFixture(
        double[] GenerationMw,
        double[] ChargeMw,
        double[] DischargeMw,
        double[] CurtailmentMw,
        double[] UnservedMw,
        double[] StateOfChargeMwh)
    {
        public IEnumerable<double[]> AllSeries =>
        [
            GenerationMw,
            ChargeMw,
            DischargeMw,
            CurtailmentMw,
            UnservedMw,
            StateOfChargeMwh.Length == 0 ? GenerationMw : StateOfChargeMwh,
        ];
    }
}