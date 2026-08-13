using System.Text.Json.Serialization;

namespace NEM.Contracts;

public sealed record InputBundleManifestDTO(
    [property: JsonPropertyName("schemaVersion")]
    int SchemaVersion,
    string BundleId,
    string Name,
    InputBundlePeriodDTO Period,
    string[] Regions);

public sealed record InputBundlePeriodDTO(
    DateTimeOffset Start,
    DateTimeOffset End);