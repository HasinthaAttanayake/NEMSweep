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
    Dictionary<string, double[]> StateOfChargeByTechnologyMwh);

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
    decimal SlcoeAudPerMwh);