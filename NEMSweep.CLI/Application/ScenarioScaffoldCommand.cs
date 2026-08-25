using NEMSweep.Contracts;

namespace NEMSweep.CLI.Application;

/// <summary>
/// Prints the smallest scenario configuration that will actually run. The published scenario is a
/// worked example of a real system and is long for good reason; this is the other thing a newcomer
/// needs, which is a file short enough to read in one go and edit without fear.
/// </summary>
/// <remarks>
/// Written to standard output rather than to a file, matching <see cref="SchemaDescriptionCommand"/>,
/// so it composes with a redirect and never overwrites something by surprise. Like that command it
/// needs no workspace, so it answers before settings are read.
/// </remarks>
internal static class ScenarioScaffoldCommand
{
    /// <summary>Writes the scaffold.</summary>
    /// <param name="output">Where the scenario is written.</param>
    public static int Run(TextWriter output)
    {
        output.WriteLine(Scaffold);
        return 0;
    }

    /// <summary>
    /// One region, one generating fleet, one storage fleet. The file names are what the data root is
    /// searched for, and the storage sizing block is what decides whether the run grows storage to
    /// meet the reliability target or reports why it could not.
    /// </summary>
    /// <remarks>
    /// The fleet is a single dispatchable generator sized to serve the region, so a first run
    /// finishes cleanly rather than greeting a newcomer with an unserved-energy warning they have no
    /// context for yet. Replace it: that is the point of the file.
    /// </remarks>
    /// <summary>
    /// Where an editor fetches the schema from. The repository is the host: the raw endpoint serves
    /// with a permissive cross-origin header and editors do not mind that it is text/plain, so
    /// hosting the file anywhere else would be presentation rather than capability.
    /// </summary>
    private static readonly string SchemaUrl =
        "https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/"
        + $"scenario-v{ArtifactSchemaVersions.ScenarioConfig}.json";

    private static readonly string Scaffold = $$"""
        {
          "$schema": "{{SchemaUrl}}",
          "schemaVersion": {{ArtifactSchemaVersions.ScenarioConfig}},
          "id": "my-scenario",
          "name": "My scenario",
          "costBasis": {
            "year": 2026,
            "realDiscountRate": 0.07
          },
          "storageSizing": {
            "maximumPowerMw": 10000,
            "maximumEnergyMwh": 100000,
            "targetUsePercentage": 0.002,
            "maximumPasses": 512,
            "reliabilityStandardName": "NEM reliability standard"
          },
          "regions": [
            {
              "regionId": "NSW1",
              "demandFile": "demand-nsw1.json",
              "weatherFile": "weather-nsw1.json",
              "generatingFleets": [
                {
                  "technology": "Gas",
                  "nameplateCapacityMw": 15000.0,
                  "costParameters": {
                    "capitalCostAudPerMw": 1600000,
                    "fixedOperatingCostAudPerMwYear": 15000,
                    "variableOperatingCostAudPerMwh": 8,
                    "fuelPriceAudPerGj": 12
                  },
                  "technologyProfile": {
                    "heatRateGjPerMwh": 7.1,
                    "technicalLifeYears": 30
                  }
                }
              ],
              "storageFleets": [
                {
                  "technology": "Battery",
                  "initialEnergyCapacityMwh": 0,
                  "initialPowerCapacityMw": 0,
                  "costParameters": {
                    "powerCapitalCostAudPerMw": 470000,
                    "energyCapitalCostAudPerMwh": 250000,
                    "fixedOperatingCostAudPerMwYear": 15000
                  },
                  "technologyProfile": {
                    "technicalLifeYears": 20,
                    "roundTripEfficiency": 0.87
                  }
                }
              ]
            }
          ]
        }
        """;
}
