using System.Text.Json;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.Scenarios;
using NEMSweep.Contracts;
using NEMSweep.Model.Grid;
using NEMSweep.Model.StorageSizing;
using NEMSweep.Model.Units;
using ContractsScenario = NEMSweep.Contracts.Scenario;

namespace NEMSweep.CLI.Tests.Scenarios;

/// <summary>
/// One real single-region publication to project star schema tables from. Real rather than
/// hand-built, so the tables are exercised against the shapes dispatch actually produces.
/// </summary>
internal sealed class StarSchemaFixture : IDisposable
{
    private const int Hours = 8_760;
    private static readonly DateTimeOffset Start = new(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    public StarSchemaFixture()
    {
        Directory = Path.Combine(Path.GetTempPath(), $"nemsweep-star-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Directory);
        WriteDemand();
        WriteWeather();

        WorkspacePaths paths = WorkspacePaths.FromRoots(Directory, Directory, Directory);
        ScenarioDispatchResult dispatch = ScenarioRunner.RunDispatch(WriteAndLoadScenario(), paths);
        PowerSystem = dispatch.PowerSystem;
        Publication = DispatchResultsExport.WritePublication(
            new DispatchPublicationRequest(
                dispatch,
                new StorageSizingOptions(
                    Power.FromMegawatts(100),
                    Energy.FromMegawattHours(400),
                    0.002,
                    4),
                null),
            Path.Combine(Directory, "results.json"));
    }

    public string Directory { get; }

    public DispatchPublication Publication { get; }

    public PowerSystem PowerSystem { get; }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

    private ScenarioSettings WriteAndLoadScenario()
    {
        string path = Path.Combine(Directory, "scenario.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "schemaVersion": {{ArtifactSchemaVersions.ScenarioConfig}},
              "id": "star-schema",
              "name": "Star schema fixture",
              "costBasis": { "year": 2026, "realDiscountRate": 0.07 },
              "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 },
              "regions": [{
                "regionId": "NSW1",
                "demandFile": "demand.json",
                "weatherFile": "weather.json",
                "generatingFleets": [{
                  "technology": "Gas",
                  "nameplateCapacityMw": 100,
                  "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 },
                  "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30, "emissionsIntensityTonnesPerMwh": 0.4 }
                }],
                "storageFleets": [{
                  "technology": "Battery",
                  "initialEnergyCapacityMwh": 0,
                  "initialPowerCapacityMw": 0,
                  "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 },
                  "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 }
                }]
              }]
            }
            """);
        return ScenarioConfig.Load(path);
    }

    private void WriteDemand() =>
        File.WriteAllText(
            Path.Combine(Directory, "demand.json"),
            JsonSerializer.Serialize(new ModelInputOutputDTO(
                ArtifactSchemaVersions.OperationalDemand,
                new ContractsScenario("test", "NSW1", Start, Start.AddYears(1), TimeSpan.FromHours(1), "hourly"),
                Start.ToUniversalTime(),
                new Sources(["source.zip"]),
                new Series(Enumerable.Repeat(10d, Hours).ToArray()))));

    private void WriteWeather()
    {
        double[] zeroes = new double[Hours];
        File.WriteAllText(
            Path.Combine(Directory, "weather.json"),
            JsonSerializer.Serialize(new WeatherDataDTO(
                ArtifactSchemaVersions.Weather,
                "NSW1",
                Start,
                TimeSpan.FromHours(1),
                new SolarWeatherData(
                    "solar.epw",
                    new WeatherLocation("Test", "00000", -33.9, 151.2),
                    zeroes,
                    zeroes,
                    zeroes,
                    zeroes,
                    Enumerable.Repeat(20d, Hours).ToArray(),
                    zeroes),
                new WindWeatherData(
                    "wind.epw",
                    new WeatherLocation("Test", "00000", -33.9, 151.2),
                    Enumerable.Repeat(5d, Hours).ToArray(),
                    10,
                    zeroes))));
    }
}
