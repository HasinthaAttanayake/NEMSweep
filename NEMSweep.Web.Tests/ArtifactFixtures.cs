using NEMSweep.Contracts;

namespace NEMSweep.Web.Tests;

/// <summary>
/// Minimal valid artifacts for tests to vary with <c>with</c> expressions. Kept in one place so a
/// contract change costs one edit rather than one per test file.
/// </summary>
public static class ArtifactFixtures
{
    public static readonly DateTimeOffset PeriodStart =
        new(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    public static WeatherBasisDTO WeatherBasis() => new(
        WeatherBasisKind.TypicalMeteorologicalYear,
        new WeatherSiteDTO("sydney-solar.epw", "Sydney (WMO 947680)"),
        new WeatherSiteDTO("sydney-wind.epw", "Sydney (WMO 947680)"),
        "Typical meteorological year from sydney.epw.");

    public static SweepScopeDTO Scope() => new(
        ["NSW1"],
        PeriodStart,
        PeriodStart.AddYears(1),
        TimeSpan.FromHours(1),
        WeatherBasis());

    public static SweepPointScalarResultsDTO Scalars(
        double storageEnergyMwh = 1,
        double? renewableShareNative = null) => new(
        SlcoeAudPerMwh: 1m,
        GenerationSlcoeAudPerMwh: 1m,
        StorageSlcoeAudPerMwh: 0m,
        DemandMwh: 1,
        EnergyServedMwh: 1,
        DeliveredGenerationMwh: 1,
        AchievedRenewableShareGridScale: null,
        AchievedRenewableShareNative: renewableShareNative,
        StoragePowerMw: 1,
        StorageEnergyMwh: storageEnergyMwh,
        UnservedEnergyMwh: 0,
        UnservedEnergyPercentageOfDemand: 0,
        UnservedHours: 0,
        HoursServedFraction: 1,
        PeakUnservedPowerMw: 0,
        CurtailedEnergyMwh: 0,
        TransmissionSlcotAudPerMwh: 0m,
        TransmissionCostStatus: TransmissionCostStatus.NotModelled,
        NetImportedEnergyMwh: 0);

    public static ReliabilityBasisDTO Reliability(bool withinTarget = true) =>
        new(0.002, withinTarget ? 0 : 0.01, withinTarget, "NEM reliability standard");

    public static StorageSizingOutcomeDTO Sizing(
        StorageSizingOutcome outcome = StorageSizingOutcome.NotRequired) =>
        new(outcome, 5515, 940, outcome == StorageSizingOutcome.Resized ? 8000 : 5515, 940, 100000, 10000, 1);

    public static SweepIndexPointDTO SucceededPoint(
        string pointId,
        string label,
        double axisValue,
        double storageEnergyMwh = 1) => new(
        pointId,
        label,
        axisValue,
        SweepPointStatus.Succeeded,
        $"points/{pointId}.json",
        $"configs/{pointId}.json",
        Scalars(storageEnergyMwh),
        Reliability(),
        Sizing(),
        new IntervalPointersDTO(null, null, 0),
        null);

    public static SweepIndexPointDTO FailedPoint(
        string pointId,
        string label,
        double axisValue,
        string message,
        SweepFailureStage stage = SweepFailureStage.Sizing,
        string code = "batteryCapacityLimitReached") => new(
        pointId,
        label,
        axisValue,
        SweepPointStatus.Failed,
        null,
        $"configs/{pointId}.json",
        null,
        null,
        null,
        null,
        new SweepPointFailureDTO(stage, code, message));

    public static SweepIndexDTO Index(params SweepIndexPointDTO[] points) => new(
        ArtifactSchemaVersions.SweepIndex,
        "test-sweep",
        "Test sweep",
        new SweepAxisDTO("Added demand", "MW"),
        Scope(),
        new SweepProvenanceDTO(
            "commit",
            false,
            "definition-hash",
            [new SweepInputFileDTO("input.json", "input", "input-hash")],
            new Dictionary<string, int> { ["sweepIndex"] = ArtifactSchemaVersions.SweepIndex }),
        points);

    public static DispatchSeriesDTO Series(
        double[] unserved,
        double[] curtailment,
        Dictionary<string, double[]> stateOfCharge) => new(
        new DispatchDemandDTO(new double[unserved.Length], [], new double[unserved.Length]),
        [],
        curtailment,
        unserved,
        new double[unserved.Length],
        new double[unserved.Length],
        stateOfCharge,
        new double[unserved.Length],
        new double[unserved.Length],
        new double[unserved.Length]);

    public static DispatchResultsDTO Results(
        int intervals = 3,
        DispatchSeriesDTO? series = null,
        IntervalPointersDTO? pointers = null,
        DispatchScenarioDTO? scenario = null) => new(
        ArtifactSchemaVersions.DispatchResults,
        scenario ?? Scenario(intervals),
        DateTimeOffset.UnixEpoch,
        new DispatchSourcesDTO(
            new DispatchInputArtifactDTO("demand-data.json", ArtifactSchemaVersions.OperationalDemand, "demand-hash"),
            new DispatchInputArtifactDTO("weather-data.json", ArtifactSchemaVersions.Weather, "weather-hash"),
            WeatherBasis(),
            []),
        new DispatchPowerSystemDTO("system", [], []),
        series ?? Series(new double[intervals], new double[intervals], []),
        new DispatchMetricsDTO(0, 0, 0, 0, 0, 0, 1, 0, pointers ?? new IntervalPointersDTO(null, null, null)),
        Reliability(),
        Sizing(),
        new DispatchCostDTO(
            "calculated", 0, 0, 0, 0, 0, 0, 0, 0,
            TransmissionCostStatus.NotModelled, 0, []));

    public static SystemDispatchResultsDTO SystemResults(
        int intervals = 3,
        string runId = "run-1",
        DispatchInterconnectorDTO[]? interconnectors = null)
    {
        DispatchResultsDTO regional = Results(intervals);
        return new SystemDispatchResultsDTO(
            ArtifactSchemaVersions.SystemDispatchResults,
            runId,
            regional.Scenario.PeriodStart,
            regional.Scenario.PeriodEnd,
            regional.Scenario.Resolution,
            ["NSW1"],
            new Dictionary<string, DispatchSourcesDTO> { ["NSW1"] = regional.DataSources },
            new Dictionary<string, RegionDispatchSummaryDTO>
            {
                ["NSW1"] = new RegionDispatchSummaryDTO(
                    regional.Metrics,
                    regional.Reliability,
                    regional.StorageSizing,
                    regional.Cost,
                    [],
                    "results-nsw1.json",
                    "results-nsw1-overview.json"),
            },
            regional.DataSeries,
            regional.Metrics,
            regional.Reliability,
            regional.StorageSizing,
            regional.Cost,
            new DispatchTopologyDTO(["NSW1"], []),
            interconnectors ?? []);
    }

    public static RegionDispatchResultsDTO RegionResults(int intervals = 3, string runId = "run-1")
    {
        DispatchResultsDTO regional = Results(intervals);
        return new RegionDispatchResultsDTO(
            ArtifactSchemaVersions.RegionDispatchResults,
            runId,
            "NSW1",
            regional.Scenario.PeriodStart,
            regional.Scenario.PeriodEnd,
            regional.Scenario.Resolution,
            regional.DataSources,
            regional.PowerSystem,
            regional.DataSeries,
            regional.Metrics,
            regional.Reliability,
            regional.StorageSizing,
            regional.Cost);
    }

    public static DispatchScenarioDTO Scenario(int intervals = 3) => new(
        "scenario",
        "Scenario",
        "NSW1",
        PeriodStart,
        PeriodStart.AddHours(intervals),
        TimeSpan.FromHours(1));
}
