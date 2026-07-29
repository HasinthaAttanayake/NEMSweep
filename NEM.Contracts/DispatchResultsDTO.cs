namespace NEM.Contracts;

public sealed record DispatchResultsDTO(
    int SchemaVersion,
    DispatchScenarioDTO Scenario,
    DateTimeOffset GeneratedAt,
    DispatchSourcesDTO DataSources,
    DispatchAssumptionsDTO Assumptions,
    DispatchSeriesDTO DataSeries,
    DispatchMetricsDTO Metrics,
    DispatchCostDTO Cost);

public sealed record DispatchScenarioDTO(
    string Id,
    string Region,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    TimeSpan Resolution);

public sealed record DispatchSourcesDTO(
    string[] DemandSourceFiles,
    string WeatherSourceFile);

public sealed record DispatchAssumptionsDTO(
    string Description,
    DispatchFleetDTO[] Fleets);

public sealed record DispatchFleetDTO(
    string Technology,
    double NameplateCapacityMw);

public sealed record DispatchSeriesDTO(
    double[] DemandMw,
    Dictionary<string, double[]> GenerationByTechnologyMw,
    double[] CurtailmentMw,
    double[] UnservedDemandMw);

public sealed record DispatchMetricsDTO(
    double DemandMwh,
    double DeliveredGenerationMwh,
    double CurtailedEnergyMwh,
    double UnservedEnergyMwh,
    int UnservedHours,
    double HoursServedFraction);

public sealed record DispatchCostDTO(
    string Status,
    double? GenerationCostAud,
    double? GenerationSlcoeAudPerMwh);