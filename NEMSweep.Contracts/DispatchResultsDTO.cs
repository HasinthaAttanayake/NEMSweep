using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>
/// Full dispatch evidence for one scenario run of <c>nem scenario run</c> (<c>results.json</c>).
/// This is the single-scenario artifact that predates the system/region split: it always has
/// exactly one region in scope, so system and region evidence coincide in it. A whole-system or
/// per-region sweep result instead uses <see cref="SystemDispatchResultsDTO"/> or
/// <see cref="RegionDispatchResultsDTO"/>. Series values are interval values in MW or MWh as
/// named; integrated metrics use MWh, reliability values are percentages of demand, and cost
/// values use AUD or AUD/MWh as named.
/// </summary>
/// <param name="SchemaVersion">Schema version of this artifact; see <see cref="ArtifactSchemaVersions.DispatchResults"/>.</param>
/// <param name="Scenario">The scenario and period this run dispatched.</param>
/// <param name="GeneratedAt">When this artifact was generated, in UTC.</param>
/// <param name="DataSources">Provenance for the demand and weather inputs the run consumed.</param>
/// <param name="PowerSystem">The realised fleet the scenario was dispatched against.</param>
/// <param name="DataSeries">The interval-by-interval dispatch series.</param>
/// <param name="Metrics">Integrated energy totals and reliability diagnostics for the run.</param>
/// <param name="Reliability">The reliability target this run was sized against and the outcome.</param>
/// <param name="StorageSizing">What, if anything, the storage sizing loop did to meet the target.</param>
/// <param name="Cost">Annualised and levelised cost evidence for the run.</param>
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

/// <summary>The scenario identity and dispatched period for a single-scenario dispatch result.</summary>
/// <param name="Id">Scenario identifier.</param>
/// <param name="Name">Human-readable scenario name.</param>
/// <param name="Region">The single NEM region this scenario dispatches.</param>
/// <param name="PeriodStart">Start of the dispatched period, in NEM market time (UTC+10).</param>
/// <param name="PeriodEnd">End of the dispatched period, in NEM market time (UTC+10).</param>
/// <param name="Resolution">Interval length of every series in this artifact.</param>
public sealed record DispatchScenarioDTO(
    string Id,
    string Name,
    string Region,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    TimeSpan Resolution);

/// <summary>
/// The real-dollar valuation basis every cost figure in a dispatch artifact is expressed against.
/// </summary>
/// <param name="Year">The calendar year whose real Australian dollars every cost figure represents.</param>
/// <param name="RealDiscountRate">The dimensionless real discount rate, as a fraction: 0.07 means 7%.</param>
public sealed record DispatchCostBasisDTO(
    int Year,
    decimal RealDiscountRate);

/// <summary>Input provenance for a single-scenario dispatch result.</summary>
/// <param name="DemandInput">Filename, schema version, and digest of the demand artifact consumed.</param>
/// <param name="WeatherInput">Filename, schema version, and digest of the weather artifact consumed.</param>
/// <param name="WeatherBasis">What the weather input represents, e.g. a typical meteorological year.</param>
/// <param name="DemandSourceFiles">
/// Descriptive upstream demand archive filenames, carried through from the demand artifact. This
/// is provenance only and does not replace <see cref="DemandInput"/>'s digest.
/// </param>
public sealed record DispatchSourcesDTO(
    DispatchInputArtifactDTO DemandInput,
    DispatchInputArtifactDTO WeatherInput,
    WeatherBasisDTO WeatherBasis,
    string[] DemandSourceFiles);

/// <summary>Identity of one input artifact a dispatch run consumed.</summary>
/// <param name="FileName">Filename of the input artifact as configured.</param>
/// <param name="SchemaVersion">Schema version declared by the input artifact.</param>
/// <param name="Sha256">SHA-256 digest of the exact bytes parsed; the reproducibility boundary if the configured path is later overwritten.</param>
public sealed record DispatchInputArtifactDTO(
    string FileName,
    int SchemaVersion,
    string Sha256);

/// <summary>The realised generation and storage fleets a single-scenario run dispatched against.</summary>
/// <param name="Id">Identifier of the realised power system.</param>
/// <param name="Fleets">Every generating fleet in the region, in no particular order.</param>
/// <param name="StorageFleets">Every storage fleet in the region, in no particular order.</param>
public sealed record DispatchPowerSystemDTO(
    string Id,
    DispatchFleetDTO[] Fleets,
    DispatchStorageFleetDTO[] StorageFleets);

/// <summary>
/// One generating fleet's identity, installed capacity, and the scenario cost assumptions it was
/// costed against. The cost fields are the scenario author's own values, not model constants; see
/// <c>docs/assumptions/scenario-parameters.md</c>.
/// </summary>
/// <param name="Technology">The generation technology, e.g. <c>Coal</c>, <c>Solar</c>, <c>Hydro</c>.</param>
/// <param name="NameplateCapacityMw">Installed nameplate capacity in MW; not a dispatch series.</param>
/// <param name="CapitalCostAudPerMw">Overnight capital cost per MW of nameplate capacity.</param>
/// <param name="FixedOperatingCostAudPerMwYear">Annual fixed operating cost per MW of nameplate capacity.</param>
/// <param name="VariableOperatingCostAudPerMwh">Variable operating cost per MWh generated.</param>
/// <param name="FuelPriceAudPerGj">Fuel price per GJ thermal, multiplied by heat rate to cost fuel.</param>
/// <param name="HeatRateGjPerMwh">Thermal energy consumed per MWh of electricity generated.</param>
/// <param name="TechnicalLifeYears">Operating life used to annuitise capital cost.</param>
public sealed record DispatchFleetDTO(
    string Technology,
    double NameplateCapacityMw,
    decimal CapitalCostAudPerMw = 0,
    decimal FixedOperatingCostAudPerMwYear = 0,
    decimal VariableOperatingCostAudPerMwh = 0,
    decimal FuelPriceAudPerGj = 0,
    double HeatRateGjPerMwh = 0,
    uint TechnicalLifeYears = 0);

/// <summary>
/// One storage fleet's identity, installed capacity, and the scenario cost assumptions it was
/// costed against. The cost fields are the scenario author's own values, not model constants; see
/// <c>docs/assumptions/scenario-parameters.md</c>.
/// </summary>
/// <param name="Technology">The storage technology, e.g. <c>Battery</c>, <c>PumpedHydro</c>.</param>
/// <param name="EnergyCapacityMwh">Installed energy capacity in MWh.</param>
/// <param name="PowerCapacityMw">Installed power capacity in MW.</param>
/// <param name="PowerCapitalCostAudPerMw">Overnight capital cost per MW of power capacity.</param>
/// <param name="EnergyCapitalCostAudPerMwh">Overnight capital cost per MWh of storage capacity.</param>
/// <param name="FixedOperatingCostAudPerMwYear">Annual fixed operating cost per MW of power capacity.</param>
/// <param name="RoundTripEfficiency">Fraction of charging energy that survives a full charge-discharge cycle.</param>
/// <param name="TechnicalLifeYears">Operating life used to annuitise capital cost.</param>
public sealed record DispatchStorageFleetDTO(
    string Technology,
    double EnergyCapacityMwh,
    double PowerCapacityMw,
    decimal PowerCapitalCostAudPerMw = 0,
    decimal EnergyCapitalCostAudPerMwh = 0,
    decimal FixedOperatingCostAudPerMwYear = 0,
    double RoundTripEfficiency = 0,
    uint TechnicalLifeYears = 0);

/// <summary>
/// The region's demand series. Total demand, not base demand alone, is what dispatch serves.
/// </summary>
/// <param name="BaseDemandMw">
/// Interval-average MW of base demand, before additive components. Null when the series has been
/// externalized to a separate regular-series file instead of inlined; when null,
/// <see cref="BaseDemandSeriesPath"/> names that file.
/// </param>
/// <param name="AdditiveComponentsByNameMw">
/// Interval-average MW of each named additive demand component (e.g. a data-centre load), keyed
/// by component name.
/// </param>
/// <param name="TotalDemandMw">
/// Interval-average MW of base demand plus every additive component; the element-wise sum
/// dispatch actually serves.
/// </param>
/// <param name="BaseDemandSeriesPath">
/// Path to the externalized base-demand series file when <see cref="BaseDemandMw"/> is null;
/// null when the series is inlined.
/// </param>
public sealed record DispatchDemandDTO(
    double[]? BaseDemandMw,
    Dictionary<string, double[]> AdditiveComponentsByNameMw,
    double[] TotalDemandMw,
    string? BaseDemandSeriesPath = null);

/// <summary>
/// Interval-by-interval dispatch evidence for a single-scenario run. Every series is
/// interval-average MW and integrates to MWh via <c>FlowSeries.Integrate()</c>. The dispatch
/// identity is <c>generation + discharge + imports + unserved = demand + charge + exports +
/// curtailment</c>; this artifact always has zero imports, exports, and transmission losses
/// because it covers exactly one unlinked region.
/// </summary>
/// <param name="Demand">The region's demand series.</param>
/// <param name="DeliveredGenerationByTechnologyMw">
/// Per-technology delivered generation, keyed by technology name: generation net of curtailment
/// and of energy diverted to charging, using the same bookkeeping allocation as
/// <c>PerFleetDelivered</c>. Not generation minus curtailment.
/// </param>
/// <param name="CurtailmentMw">Total generation curtailed rather than delivered or stored.</param>
/// <param name="UnservedDemandMw">Demand not served by generation, storage discharge, or imports.</param>
/// <param name="ChargeMw">Total storage charging power, across every storage technology.</param>
/// <param name="DischargeMw">Total storage discharge power, across every storage technology.</param>
/// <param name="StateOfChargeByTechnologyMwh">
/// Interval-beginning state of charge in MWh, keyed by storage technology.
/// </param>
/// <param name="ImportsMw">Imported power; always zero in this unlinked single-region artifact.</param>
/// <param name="ExportsMw">Exported power; always zero in this unlinked single-region artifact.</param>
/// <param name="TransmissionLossesMw">Transmission loss; always zero in this unlinked single-region artifact.</param>
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
/// <see cref="DistanceKm"/> is the line's declared route length, the same value used to cost it.
/// The From/To latitude and longitude are each region's solar weather site, the location already
/// carried by its resource profile, and are for map placement only, not costing.
/// </remarks>
public sealed record DispatchInterconnectorDTO(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string FromRegionId,
    [property: JsonRequired] string ToRegionId,
    [property: JsonRequired] double CapacityMw,
    [property: JsonRequired] double[] FlowMw,
    [property: JsonRequired] double[] LossesMw,
    [property: JsonRequired] double DistanceKm = 0,
    [property: JsonRequired] double FromLatitude = 0,
    [property: JsonRequired] double FromLongitude = 0,
    [property: JsonRequired] double ToLatitude = 0,
    [property: JsonRequired] double ToLongitude = 0,
    decimal CapitalCostAudPerKmPerMw = 0,
    decimal FixedOperatingCostAudPerKmPerMwYear = 0,
    uint TechnicalLifeYears = 0);

/// <summary>
/// Integrated energy totals and reliability diagnostics for a single-scenario dispatch run.
/// </summary>
/// <param name="DemandMwh">Total demand energy over the period (base demand plus additive components).</param>
/// <param name="DeliveredGenerationMwh">
/// Total delivered generation energy, summed from the same per-fleet allocation as
/// <see cref="DispatchSeriesDTO.DeliveredGenerationByTechnologyMw"/>. Not generation minus curtailment.
/// </param>
/// <param name="CurtailedEnergyMwh">Total curtailed generation energy over the period.</param>
/// <param name="UnservedEnergyMwh">Total unserved energy (USE) over the period.</param>
/// <param name="UnservedEnergyPercentageOfDemand">
/// USE as a percentage of demand energy. This is the binding reliability measure; it is what a
/// reliability target is expressed and checked against.
/// </param>
/// <param name="UnservedHours">
/// Count of hours with any unserved demand. A diagnostic, not to be compared directly with an
/// energy-based reliability target.
/// </param>
/// <param name="HoursServedFraction">
/// Fraction of hours with zero unserved demand. A diagnostic, not to be compared directly with an
/// energy-based reliability target.
/// </param>
/// <param name="PeakUnservedPowerMw">
/// The single highest-power interval of unserved demand, in MW. A diagnostic, not an energy
/// quantity, and not to be compared directly with an energy-based reliability target.
/// </param>
/// <param name="IntervalPointers">Indices of the intervals worth opening a run at.</param>
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

/// <summary>
/// Annualised and levelised cost evidence for a single-scenario dispatch run. These are modelled
/// estimates, not audited figures. Every levelised (SLCoE) rate divides by energy served,
/// i.e. demand minus unserved energy, not by gross generation.
/// </summary>
/// <param name="Status">
/// Legacy free-text cost-calculation status, always <c>"calculated"</c> in current writers. Not
/// to be confused with <see cref="TransmissionCostStatus"/>, which distinguishes whether
/// transmission cost was in scope.
/// </param>
/// <param name="AnnualisedGenerationCostAud">Total annualised generation capex, fixed OPEX, variable OPEX, and fuel cost, in AUD/year.</param>
/// <param name="AnnualisedStorageCostAud">Total annualised storage capex and fixed OPEX, in AUD/year.</param>
/// <param name="TotalAnnualisedCostAud">Generation plus storage annualised cost, in AUD/year. Excludes transmission; see <see cref="AnnualisedTransmissionCostAud"/>.</param>
/// <param name="GenerationSlcoeAudPerMwh">Annualised generation cost divided by energy served, in AUD/MWh.</param>
/// <param name="StorageSlcoeAudPerMwh">Annualised storage cost divided by energy served, in AUD/MWh. Not a standalone levelised cost of storage; storage charging energy is already priced into generation VOM and fuel cost.</param>
/// <param name="SlcoeAudPerMwh">Total system levelised cost of electricity: generation plus storage SLCoE, in AUD/MWh.</param>
/// <param name="AnnualisedTransmissionCostAud">Annualised transmission cost, in AUD/year; zero when <see cref="TransmissionCostStatus"/> is <see cref="Contracts.TransmissionCostStatus.NotModelled"/>.</param>
/// <param name="TransmissionSlcotAudPerMwh">Annualised transmission cost divided by energy served, in AUD/MWh.</param>
/// <param name="TransmissionCostStatus">Whether transmission cost was calculated for this run or left out of scope.</param>
/// <param name="NetImportedEnergyMwh">Imported energy minus exported energy over the period, in MWh; always zero for this unlinked single-region artifact.</param>
/// <param name="GenerationCostContributions">Annualised and levelised generation cost broken down by technology; sums to <see cref="AnnualisedGenerationCostAud"/>.</param>
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
    /// <summary>Transmission cost was not calculated; this artifact's cost scope excludes it entirely.</summary>
    [JsonStringEnumMemberName("notModelled")]
    NotModelled,

    /// <summary>Transmission cost was calculated and is included in this artifact's cost fields.</summary>
    [JsonStringEnumMemberName("calculated")]
    Calculated,
}

/// <summary>Annualised and levelised generation cost attributable to one technology.</summary>
public sealed record DispatchGenerationCostContributionDTO(
    [property: JsonRequired] string Technology,
    [property: JsonRequired] decimal AnnualisedCostAud,
    [property: JsonRequired] decimal LevelisedContributionAudPerMwh);