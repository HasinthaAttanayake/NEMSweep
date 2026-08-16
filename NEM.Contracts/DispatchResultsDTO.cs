using System.Text.Json.Serialization;

namespace NEM.Contracts;

public sealed record DispatchResultsDTO(
    int SchemaVersion,
    DispatchScenarioDTO Scenario,
    DateTimeOffset GeneratedAt,
    DispatchSourcesDTO DataSources,
    DispatchPowerSystemDTO PowerSystem,
    DispatchSeriesDTO DataSeries,
    DispatchMetricsDTO Metrics,
    ReliabilityBasisDTO Reliability,
    StorageSizingOutcomeDTO StorageSizing,
    DispatchCostDTO Cost);

public sealed record DispatchScenarioDTO(
    string Id,
    string Name,
    string Region,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    TimeSpan Resolution);

public sealed record DispatchSourcesDTO(
    DispatchInputArtifactDTO DemandInput,
    DispatchInputArtifactDTO WeatherInput,
    WeatherBasisDTO WeatherBasis,
    string[] DemandSourceFiles);

public sealed record DispatchInputArtifactDTO(
    string FileName,
    int SchemaVersion,
    string Sha256);

public sealed record DispatchPowerSystemDTO(
    string Id,
    DispatchFleetDTO[] Fleets,
    DispatchStorageFleetDTO[] StorageFleets);

public sealed record DispatchFleetDTO(
    string Technology,
    double NameplateCapacityMw);

public sealed record DispatchStorageFleetDTO(
    string Technology,
    double EnergyCapacityMwh,
    double PowerCapacityMw);

public sealed record DispatchDemandDTO(
    double[]? BaseDemandMw,
    Dictionary<string, double[]> AdditiveComponentsByNameMw,
    double[] TotalDemandMw,
    string? BaseDemandSeriesPath = null);

public sealed record DispatchSeriesDTO(
    DispatchDemandDTO Demand,
    Dictionary<string, double[]> DeliveredGenerationByTechnologyMw,
    double[] CurtailmentMw,
    double[] UnservedDemandMw,
    double[] ChargeMw,
    double[] DischargeMw,
    Dictionary<string, double[]> StateOfChargeByTechnologyMwh,
    [property: JsonRequired] double[] ImportsMw,
    [property: JsonRequired] double[] ExportsMw,
    [property: JsonRequired] double[] TransmissionLossesMw);

/// <summary>Directed interconnector evidence retained in a system artifact.</summary>
/// <remarks>
/// <see cref="DistanceKm"/> is the great-circle distance between the endpoint regions' weather
/// sites, used to cost the line by route length. The From/To latitude and longitude are each
/// region's solar weather site, the location already carried by its resource profile.
/// </remarks>
public sealed record DispatchInterconnectorDTO(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string FromRegionId,
    [property: JsonRequired] string ToRegionId,
    [property: JsonRequired] double CapacityMw,
    [property: JsonRequired] double[] FlowMw,
    [property: JsonRequired] double[] LossesMw,
    double DistanceKm = 0,
    double FromLatitude = 0,
    double FromLongitude = 0,
    double ToLatitude = 0,
    double ToLongitude = 0);

public sealed record DispatchMetricsDTO(
    double DemandMwh,
    double DeliveredGenerationMwh,
    double CurtailedEnergyMwh,
    double UnservedEnergyMwh,
    double UnservedEnergyPercentageOfDemand,
    int UnservedHours,
    double HoursServedFraction,
    double PeakUnservedPowerMw,
    IntervalPointersDTO IntervalPointers);

public sealed record DispatchCostDTO(
    string Status,
    decimal AnnualisedGenerationCostAud,
    decimal AnnualisedStorageCostAud,
    decimal TotalAnnualisedCostAud,
    decimal GenerationSlcoeAudPerMwh,
    decimal StorageSlcoeAudPerMwh,
    decimal SlcoeAudPerMwh,
    [property: JsonRequired] decimal AnnualisedTransmissionCostAud,
    [property: JsonRequired] decimal TransmissionSlcotAudPerMwh,
    [property: JsonRequired] TransmissionCostStatus TransmissionCostStatus,
    [property: JsonRequired] double NetImportedEnergyMwh,
    [property: JsonRequired] DispatchGenerationCostContributionDTO[] GenerationCostContributions);

/// <summary>Whether transmission costs are included in this artifact's cost scope.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransmissionCostStatus>))]
public enum TransmissionCostStatus
{
    [JsonStringEnumMemberName("notModelled")]
    NotModelled,

    [JsonStringEnumMemberName("calculated")]
    Calculated,
}

/// <summary>Annualised and levelised generation cost attributable to one technology.</summary>
public sealed record DispatchGenerationCostContributionDTO(
    [property: JsonRequired] string Technology,
    [property: JsonRequired] decimal AnnualisedCostAud,
    [property: JsonRequired] decimal LevelisedContributionAudPerMwh);