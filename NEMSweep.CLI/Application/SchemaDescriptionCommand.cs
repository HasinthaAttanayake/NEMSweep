using System.Text.Json.Nodes;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Application;

internal static class SchemaDescriptionCommand
{
    /// <summary>Writes a format's JSON Schema. Needs no workspace, only somewhere to write.</summary>
    /// <param name="output">Where the schema is written.</param>
    /// <param name="format">Either <c>scenario</c> or <c>sweep</c>.</param>
    public static int Run(TextWriter output, string format)
    {
        string schema = format switch
        {
            "scenario" => ScenarioSchema,
            "sweep" => SweepSchema,
            _ => throw new ArgumentException("Schema format must be 'scenario' or 'sweep'."),
        };

        output.WriteLine(JsonFile.SerializeExact(JsonNode.Parse(schema)!));
        return 0;
    }

    private static readonly string ScenarioSchema = $$"""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://nemsweep.com/schemas/scenario-config.schema.json",
          "title": "NEMSweep scenario configuration",
          "type": "object",
          "additionalProperties": false,
          "required": ["schemaVersion", "id", "name", "costBasis", "regions", "storageSizing"],
          "properties": {
            "schemaVersion": { "const": {{ArtifactSchemaVersions.ScenarioConfig}} },
            "id": { "type": "string", "minLength": 1 },
            "name": { "type": "string", "minLength": 1 },
            "costBasis": { "$ref": "#/$defs/costBasis" },
            "regions": { "type": "array", "minItems": 1, "items": { "$ref": "#/$defs/region" } },
            "storageSizing": { "$ref": "#/$defs/storageSizing" },
            "interconnectors": { "type": "array", "items": { "$ref": "#/$defs/interconnector" } },
            "provenance": { "type": "object" }
          },
          "$defs": {
            "costBasis": {
              "type": "object", "additionalProperties": false,
              "required": ["year", "realDiscountRate"],
              "properties": {
                "year": { "type": "integer", "minimum": 2000, "maximum": 2100 },
                "realDiscountRate": { "type": "number" }
              }
            },
            "storageSizing": {
              "type": "object", "additionalProperties": false,
              "required": ["maximumPowerMw", "maximumEnergyMwh"],
              "properties": {
                "maximumPowerMw": { "type": "number" },
                "maximumEnergyMwh": { "type": "number" },
                "targetUsePercentage": { "type": "number", "exclusiveMinimum": 0, "maximum": 100 },
                "maximumPasses": { "type": "integer" },
                "reliabilityStandardName": { "type": ["string", "null"] }
              }
            },
            "region": {
              "type": "object", "additionalProperties": false,
              "required": ["regionId", "generatingFleets", "storageFleets", "demandFile", "weatherFile"],
              "properties": {
                "regionId": { "type": "string", "minLength": 1 },
                "generatingFleets": { "type": "array", "minItems": 1, "items": { "$ref": "#/$defs/generatingFleet" } },
                "storageFleets": { "type": "array", "minItems": 1, "items": { "$ref": "#/$defs/storageFleet" } },
                "demandFile": { "type": "string", "minLength": 1 },
                "weatherFile": { "type": "string", "minLength": 1 },
                "dataCentreNameplateMw": { "type": "number", "minimum": 0 }
              }
            },
            "generatingFleet": {
              "type": "object", "additionalProperties": false,
              "required": ["technology", "nameplateCapacityMw", "costParameters", "technologyProfile"],
              "properties": {
                "technology": { "type": "string", "minLength": 1 },
                "nameplateCapacityMw": { "type": "number", "minimum": 0 },
                "costParameters": { "$ref": "#/$defs/generationCosts" },
                "technologyProfile": { "$ref": "#/$defs/generationProfile" },
                "monthlyCapacityFactors": { "type": "array", "items": { "$ref": "#/$defs/monthlyCapacityFactor" } }
              }
            },
            "generationCosts": {
              "type": "object", "additionalProperties": false,
              "required": ["capitalCostAudPerMw", "fixedOperatingCostAudPerMwYear", "variableOperatingCostAudPerMwh", "fuelPriceAudPerGj"],
              "properties": {
                "capitalCostAudPerMw": { "type": "number", "minimum": 0 },
                "fixedOperatingCostAudPerMwYear": { "type": "number", "minimum": 0 },
                "variableOperatingCostAudPerMwh": { "type": "number", "minimum": 0 },
                "fuelPriceAudPerGj": { "type": "number", "minimum": 0 }
              }
            },
            "generationProfile": {
              "type": "object", "additionalProperties": false,
              "required": ["heatRateGjPerMwh", "technicalLifeYears"],
              "properties": {
                "heatRateGjPerMwh": { "type": "number", "minimum": 0 },
                "technicalLifeYears": { "type": "integer", "minimum": 0 }
              }
            },
            "monthlyCapacityFactor": {
              "type": "object", "additionalProperties": false,
              "required": ["month", "capacityFactor"],
              "properties": {
                "month": { "type": "string", "format": "date" },
                "capacityFactor": { "type": "number", "exclusiveMinimum": 0, "maximum": 1 }
              }
            },
            "storageFleet": {
              "type": "object", "additionalProperties": false,
              "required": ["technology", "initialEnergyCapacityMwh", "initialPowerCapacityMw", "costParameters", "technologyProfile"],
              "properties": {
                "technology": { "type": "string", "minLength": 1 },
                "initialEnergyCapacityMwh": { "type": "number" },
                "initialPowerCapacityMw": { "type": "number" },
                "costParameters": { "$ref": "#/$defs/storageCosts" },
                "technologyProfile": { "$ref": "#/$defs/storageProfile" }
              }
            },
            "storageCosts": {
              "type": "object", "additionalProperties": false,
              "required": ["powerCapitalCostAudPerMw", "energyCapitalCostAudPerMwh", "fixedOperatingCostAudPerMwYear"],
              "properties": {
                "powerCapitalCostAudPerMw": { "type": "number", "minimum": 0 },
                "energyCapitalCostAudPerMwh": { "type": "number", "minimum": 0 },
                "fixedOperatingCostAudPerMwYear": { "type": "number", "minimum": 0 }
              }
            },
            "storageProfile": {
              "type": "object", "additionalProperties": false,
              "required": ["technicalLifeYears", "roundTripEfficiency"],
              "properties": {
                "technicalLifeYears": { "type": "integer", "minimum": 0 },
                "roundTripEfficiency": { "type": "number", "minimum": 0, "maximum": 1 }
              }
            },
            "interconnector": {
              "type": "object", "additionalProperties": false,
              "required": ["fromRegionId", "toRegionId", "capacityMw", "routeLengthKm", "capitalCostAudPerKmPerMw", "fixedOperatingCostAudPerKmPerMwYear", "technicalLifeYears"],
              "properties": {
                "fromRegionId": { "type": "string", "minLength": 1 },
                "toRegionId": { "type": "string", "minLength": 1 },
                "capacityMw": { "type": "number", "minimum": 0 },
                "routeLengthKm": { "type": "number", "exclusiveMinimum": 0 },
                "capitalCostAudPerKmPerMw": { "type": "number", "minimum": 0 },
                "fixedOperatingCostAudPerKmPerMwYear": { "type": "number", "minimum": 0 },
                "technicalLifeYears": { "type": "integer", "minimum": 1 }
              }
            }
          }
        }
        """;

    private static readonly string SweepSchema = $$"""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://nemsweep.com/schemas/sweep-definition.schema.json",
          "title": "NEMSweep sweep definition",
          "type": "object",
          "additionalProperties": false,
          "required": ["schemaVersion", "sweepId", "name", "axis", "baselineConfigPath", "points"],
          "properties": {
            "schemaVersion": { "const": {{ArtifactSchemaVersions.SweepDefinition}} },
            "sweepId": { "type": "string", "pattern": "^[a-z0-9][a-z0-9-]*$" },
            "name": { "type": "string", "minLength": 1 },
            "axis": {
              "type": "object", "additionalProperties": false,
              "required": ["label", "unit"],
              "properties": { "label": { "type": "string", "minLength": 1 }, "unit": { "type": "string", "minLength": 1 } }
            },
            "baselineConfigPath": { "type": "string", "minLength": 1 },
            "points": {
              "type": "array", "minItems": 1,
              "items": {
                "type": "object", "additionalProperties": false,
                "required": ["pointId", "axisValue", "label", "overrides"],
                "properties": {
                  "pointId": { "type": "string", "pattern": "^[a-z0-9][a-z0-9-]*$" },
                  "axisValue": { "type": "number" },
                  "label": { "type": "string", "minLength": 1 },
                  "overrides": { "$ref": "#/$defs/scenarioOverrides" }
                }
              }
            }
          },
          "$defs": {
            "scenarioOverrides": {
              "type": "object", "additionalProperties": false,
              "properties": {
                "schemaVersion": { "type": ["integer", "null"] },
                "id": { "type": ["string", "null"] },
                "name": { "type": ["string", "null"] },
                "costBasis": { "$ref": "#/$defs/costBasisOverride" },
                "regions": { "type": ["array", "null"], "items": { "$ref": "#/$defs/regionOverride" } },
                "storageSizing": { "$ref": "#/$defs/storageSizingOverride" },
                "provenance": { "type": ["object", "null"] }
              }
            },
            "costBasisOverride": {
              "type": ["object", "null"], "additionalProperties": false,
              "properties": { "year": { "type": ["integer", "null"] }, "realDiscountRate": { "type": ["number", "null"] } }
            },
            "storageSizingOverride": {
              "type": ["object", "null"], "additionalProperties": false,
              "properties": {
                "maximumPowerMw": { "type": ["number", "null"] }, "maximumEnergyMwh": { "type": ["number", "null"] },
                "targetUsePercentage": { "type": ["number", "null"] }, "maximumPasses": { "type": ["integer", "null"] },
                "reliabilityStandardName": { "type": ["string", "null"] }
              }
            },
            "regionOverride": {
              "type": "object", "additionalProperties": false, "required": ["regionId"],
              "properties": {
                "regionId": { "type": ["string", "null"] },
                "generatingFleets": { "type": ["array", "null"], "items": { "$ref": "#/$defs/generatingFleetOverride" } },
                "storageFleets": { "type": ["array", "null"], "items": { "$ref": "#/$defs/storageFleetOverride" } },
                "demandFile": { "type": ["string", "null"] }, "weatherFile": { "type": ["string", "null"] },
                "dataCentreNameplateMw": { "type": ["number", "null"] }, "$remove": { "const": true }
              }
            },
            "generatingFleetOverride": {
              "type": "object", "additionalProperties": false, "required": ["technology"],
              "properties": {
                "technology": { "type": ["string", "null"] }, "nameplateCapacityMw": { "type": ["number", "null"] },
                "costParameters": { "$ref": "#/$defs/generationCostsOverride" }, "technologyProfile": { "$ref": "#/$defs/generationProfileOverride" },
                "monthlyCapacityFactors": { "type": ["array", "null"], "items": { "$ref": "#/$defs/monthlyCapacityFactorOverride" } }, "$remove": { "const": true }
              }
            },
            "storageFleetOverride": {
              "type": "object", "additionalProperties": false, "required": ["technology"],
              "properties": {
                "technology": { "type": ["string", "null"] }, "initialEnergyCapacityMwh": { "type": ["number", "null"] },
                "initialPowerCapacityMw": { "type": ["number", "null"] }, "costParameters": { "$ref": "#/$defs/storageCostsOverride" },
                "technologyProfile": { "$ref": "#/$defs/storageProfileOverride" }, "$remove": { "const": true }
              }
            },
            "generationCostsOverride": {
              "type": ["object", "null"], "additionalProperties": false,
              "properties": {
                "capitalCostAudPerMw": { "type": ["number", "null"] }, "fixedOperatingCostAudPerMwYear": { "type": ["number", "null"] },
                "variableOperatingCostAudPerMwh": { "type": ["number", "null"] }, "fuelPriceAudPerGj": { "type": ["number", "null"] }
              }
            },
            "generationProfileOverride": {
              "type": ["object", "null"], "additionalProperties": false,
              "properties": { "heatRateGjPerMwh": { "type": ["number", "null"] }, "technicalLifeYears": { "type": ["integer", "null"] } }
            },
            "storageCostsOverride": {
              "type": ["object", "null"], "additionalProperties": false,
              "properties": {
                "powerCapitalCostAudPerMw": { "type": ["number", "null"] }, "energyCapitalCostAudPerMwh": { "type": ["number", "null"] },
                "fixedOperatingCostAudPerMwYear": { "type": ["number", "null"] }
              }
            },
            "storageProfileOverride": {
              "type": ["object", "null"], "additionalProperties": false,
              "properties": { "technicalLifeYears": { "type": ["integer", "null"] }, "roundTripEfficiency": { "type": ["number", "null"] } }
            },
            "monthlyCapacityFactorOverride": {
              "type": "object", "additionalProperties": false, "required": ["month"],
              "properties": { "month": { "type": ["string", "null"], "format": "date" }, "capacityFactor": { "type": ["number", "null"] }, "$remove": { "const": true } }
            }
          }
        }
        """;
}