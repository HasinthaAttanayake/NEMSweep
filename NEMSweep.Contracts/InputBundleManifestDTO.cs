using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>
/// Declares what an input bundle folder contains: <c>manifest.json</c> at the bundle root.
/// Everything else in the bundle (demand archives, per-region weather, the generation-information
/// workbook) is discovered from the folder shape rather than listed here; this manifest exists so
/// the bundle can be identified and validated before that discovery runs.
/// </summary>
/// <param name="SchemaVersion">
/// Schema version of this manifest. Ingestion rejects a bundle whose version it does not support.
/// </param>
/// <param name="BundleId">
/// Identifier for the bundle. Expected to match the name of the folder the manifest lives in;
/// a mismatch is a warning, not a rejection.
/// </param>
/// <param name="Name">Human-readable name of the bundle.</param>
/// <param name="Period">Calendar period the bundle's inputs are intended to cover.</param>
/// <param name="Regions">
/// NEM region identifiers (for example <c>NSW1</c>) the bundle supplies weather for. Every entry
/// must be a recognised NEM region and the array must not be empty or contain duplicates.
/// </param>
public sealed record InputBundleManifestDTO(
    [property: JsonPropertyName("schemaVersion")]
    int SchemaVersion,
    string BundleId,
    string Name,
    InputBundlePeriodDTO Period,
    string[] Regions);

/// <summary>Calendar period a bundle's inputs are intended to cover.</summary>
/// <param name="Start">
/// Inclusive start of the period. Its UTC offset is the market-time offset the ingested demand and
/// weather are normalised to (the NEM's is UTC+10); <see cref="End"/> must carry the same offset.
/// </param>
/// <param name="End">Exclusive end of the period; must be after <see cref="Start"/> and carry the same offset.</param>
public sealed record InputBundlePeriodDTO(
    DateTimeOffset Start,
    DateTimeOffset End);
