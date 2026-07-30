namespace NEM.Contracts;

public sealed record DispatchResultsDTO(
    int SchemaVersion,
    DispatchScenarioDTO Scenario,
    DateTimeOffset GeneratedAt,
    DispatchSourcesDTO DataSources,
    DispatchPowerSystemDTO PowerSystem,
    DispatchSeriesDTO DataSeries,
    DispatchMetricsDTO Metrics,
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
    string[] DemandSourceFiles);

public sealed record DispatchInputArtifactDTO(
    string FileName,
    int SchemaVersion,
    string Sha256);

public sealed record DispatchPowerSystemDTO(
    string Id,
    DispatchFleetDTO[] Fleets);

public sealed record DispatchFleetDTO(
    string Technology,
    double NameplateCapacityMw);

public sealed record DispatchSeriesDTO(
    double[] DemandMw,
    Dictionary<string, double[]> DeliveredGenerationByTechnologyMw,
    double[] CurtailmentMw,
    double[] UnservedDemandMw);

public sealed record DispatchMetricsDTO(
    double DemandMwh,
    double DeliveredGenerationMwh,
    double CurtailedEnergyMwh,
    double UnservedEnergyMwh,
    double UnservedEnergyPercentageOfDemand,
    int UnservedHours,
    double HoursServedFraction);

public sealed record DispatchCostDTO(
    string Status,
    double? GenerationCostAud,
    double? GenerationSlcoeAudPerMwh);