using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>
/// Declares the identity, period, and source weather-directory mapping for an input bundle. Schema
/// version 1 manifests used string entries in <c>regions</c>; the CLI normalizes those entries to
/// this version 2 shape when it loads a legacy bundle.
/// </summary>
/// <param name="SchemaVersion">Schema version of this manifest.</param>
/// <param name="BundleId">Identifier for the bundle.</param>
/// <param name="Name">Human-readable name of the bundle.</param>
/// <param name="Period">Calendar period the bundle's inputs are intended to cover.</param>
/// <param name="Regions">Region identities and their corresponding weather subdirectories.</param>
public sealed record InputBundleManifestDTO(
    [property: JsonPropertyName("schemaVersion")]
    int SchemaVersion,
    string BundleId,
    string Name,
    InputBundlePeriodDTO Period,
    InputBundleRegionDTO[] Regions);

/// <summary>A bundle region identity and the safe, readable directory containing its weather.</summary>
/// <param name="RegionId">Original, free-form region identifier retained in produced data.</param>
/// <param name="WeatherDirectory">Single directory name under the bundle's <c>weather</c> folder.</param>
public sealed record InputBundleRegionDTO(
    string RegionId,
    string WeatherDirectory);

/// <summary>Calendar period a bundle's inputs are intended to cover.</summary>
/// <param name="Start">
/// Inclusive start of the period. Its UTC offset is the market-time offset the ingested demand and
/// weather are normalised to; <see cref="End"/> must carry the same offset.
/// </param>
/// <param name="End">Exclusive end of the period; must be after <see cref="Start"/> and carry the same offset.</param>
public sealed record InputBundlePeriodDTO(
    DateTimeOffset Start,
    DateTimeOffset End);
