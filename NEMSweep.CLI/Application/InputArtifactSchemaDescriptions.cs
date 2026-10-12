using NEMSweep.CLI.Configuration;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Application;

internal static class InputArtifactSchemaDescriptions
{
    private const string PublishedRoot =
        "https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema";

    public static string Manifest { get; } = $$"""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "{{PublishedRoot}}/manifest-v{{InputBundle.SupportedSchemaVersion}}.json",
          "title": "NEMSweep input bundle manifest (v2)",
          "type": "object",
          "additionalProperties": false,
          "required": ["schemaVersion", "bundleId", "name", "period", "regions"],
          "properties": {
            "$schema": { "type": "string", "format": "uri" },
            "schemaVersion": { "const": {{InputBundle.SupportedSchemaVersion}} },
            "bundleId": { "type": "string", "minLength": 1 },
            "name": { "type": "string", "minLength": 1 },
            "period": { "$ref": "#/$defs/period" },
            "regions": { "type": "array", "minItems": 1, "items": { "$ref": "#/$defs/region" } }
          },
          "$defs": {
            "period": {
              "type": "object", "additionalProperties": false,
              "required": ["start", "end"],
              "properties": {
                "start": { "type": "string", "format": "date-time" },
                "end": { "type": "string", "format": "date-time" }
              }
            },
            "region": {
              "type": "object", "additionalProperties": false,
              "required": ["regionId", "weatherDirectory"],
              "properties": {
                "regionId": { "type": "string", "minLength": 1, "pattern": "^\\S(?:.*\\S)?$", "description": "Must have no leading or trailing whitespace. Custom IDs are limited to 100 UTF-8 bytes after invariant uppercase normalization." },
                "weatherDirectory": { "type": "string", "minLength": 1, "maxLength": 255, "pattern": "^[^/\\\\:<>\"|?*]+$" }
              }
            }
          }
        }
        """;

    public static string LegacyManifest { get; } = $$"""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "{{PublishedRoot}}/legacy-manifest-v{{InputBundle.LegacySchemaVersion}}.json",
          "title": "NEMSweep legacy input bundle manifest (v1)",
          "type": "object",
          "required": ["schemaVersion", "bundleId", "name", "period", "regions"],
          "properties": {
            "schemaVersion": { "const": {{InputBundle.LegacySchemaVersion}} },
            "bundleId": { "type": "string", "minLength": 1 },
            "name": { "type": "string", "minLength": 1 },
            "period": { "$ref": "#/$defs/period" },
            "regions": { "type": "array", "minItems": 1, "items": { "type": "string", "minLength": 1 } }
          },
          "$defs": {
            "period": {
              "type": "object",
              "required": ["start", "end"],
              "properties": {
                "start": { "type": "string", "format": "date-time" },
                "end": { "type": "string", "format": "date-time" }
              }
            }
          }
        }
        """;

    public static string Demand { get; } = $$"""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "{{PublishedRoot}}/demand-v{{ArtifactSchemaVersions.OperationalDemand}}.json",
          "title": "NEMSweep operational demand artifact",
          "description": "Half-hour interval-average demand in MW; periodEnd is exclusive.",
          "type": "object",
          "required": ["schemaVersion", "scenario", "generatedAt", "dataSources", "dataSeries"],
          "properties": {
            "schemaVersion": { "const": {{ArtifactSchemaVersions.OperationalDemand}} },
            "scenario": { "$ref": "#/$defs/scenario" },
            "generatedAt": { "type": "string", "format": "date-time" },
            "dataSources": { "$ref": "#/$defs/dataSources" },
            "dataSeries": { "$ref": "#/$defs/dataSeries" }
          },
          "$defs": {
            "scenario": {
              "type": "object",
              "required": ["id", "region", "periodStart", "periodEnd", "resolution", "aggregation"],
              "properties": {
                "id": { "type": "string" },
                "region": { "type": "string" },
                "periodStart": { "type": "string", "format": "date-time" },
                "periodEnd": { "type": "string", "format": "date-time" },
                "resolution": { "type": "string", "description": "TimeSpan, emitted as 00:30:00 for half-hour demand." },
                "aggregation": { "type": "string" }
              }
            },
            "dataSources": {
              "type": "object", "required": ["demandSourceFiles"],
              "properties": {
                "demandSourceFiles": { "type": "array", "items": { "type": "string" } }
              }
            },
            "dataSeries": {
              "type": "object", "required": ["demandMw"],
              "properties": {
                "demandMw": { "type": "array", "items": { "type": "number", "minimum": 0 } }
              }
            }
          }
        }
        """;

    public static string Weather { get; } = $$"""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "{{PublishedRoot}}/weather-v{{ArtifactSchemaVersions.Weather}}.json",
          "title": "NEMSweep weather resource artifact",
          "description": "Hourly weather observations and derived generation traces for one region.",
          "type": "object",
          "required": ["schemaVersion", "regionId", "start", "resolution", "solar", "wind"],
          "properties": {
            "schemaVersion": { "const": {{ArtifactSchemaVersions.Weather}} },
            "regionId": { "type": "string", "minLength": 1 },
            "start": { "type": "string", "format": "date-time" },
            "resolution": { "type": "string", "description": "TimeSpan, emitted as 01:00:00 for hourly weather." },
            "solar": { "$ref": "#/$defs/solar" },
            "wind": { "$ref": "#/$defs/wind" }
          },
          "$defs": {
            "location": {
              "type": "object", "required": ["city", "wmo", "latitude", "longitude"],
              "properties": {
                "city": { "type": "string" },
                "wmo": { "type": "string" },
                "latitude": { "type": "number", "minimum": -90, "maximum": 90 },
                "longitude": { "type": "number", "minimum": -180, "maximum": 180 }
              }
            },
            "solar": {
              "type": "object",
              "required": ["sourceFile", "location", "globalHorizontalRadiationWhPerSquareMetre", "directNormalRadiationWhPerSquareMetre", "diffuseHorizontalRadiationWhPerSquareMetre", "solarZenithDegrees", "dryBulbTemperatureDegreesCelsius", "productionMegawattsAtOneMegawattAc"],
              "properties": {
                "sourceFile": { "type": "string" },
                "location": { "$ref": "#/$defs/location" },
                "globalHorizontalRadiationWhPerSquareMetre": { "type": "array", "items": { "type": "number" } },
                "directNormalRadiationWhPerSquareMetre": { "type": "array", "items": { "type": "number" } },
                "diffuseHorizontalRadiationWhPerSquareMetre": { "type": "array", "items": { "type": "number" } },
                "solarZenithDegrees": { "type": "array", "items": { "type": "number" } },
                "dryBulbTemperatureDegreesCelsius": { "type": "array", "items": { "type": "number" } },
                "productionMegawattsAtOneMegawattAc": { "type": "array", "items": { "type": "number" }, "description": "Recomputed from weather resource; not an input control." }
              }
            },
            "wind": {
              "type": "object",
              "required": ["sourceFile", "location", "windSpeedMetresPerSecond", "measurementHeightMetres", "productionMegawattsAtOneMegawattInstalled"],
              "properties": {
                "sourceFile": { "type": "string" },
                "location": { "$ref": "#/$defs/location" },
                "windSpeedMetresPerSecond": { "type": "array", "items": { "type": "number" } },
                "measurementHeightMetres": { "type": "number", "minimum": 0 },
                "productionMegawattsAtOneMegawattInstalled": { "type": "array", "items": { "type": "number" }, "description": "Recomputed from weather resource; not an input control." }
              }
            }
          }
        }
        """;
}