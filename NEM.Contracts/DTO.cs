using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NEM.Contracts
{
    /// <summary>
    /// The operational demand artifact (<c>demand-data.json</c>): one region's demand series over
    /// the period spanned by the archives it was assembled from, plus enough provenance to
    /// reproduce it. <see cref="SchemaVersion"/> tracks <see cref="ArtifactSchemaVersions.OperationalDemand"/>.
    /// </summary>
    public class ModelInputOutputDTO
    {
        /// <summary>Constructs a populated instance; every argument is required.</summary>
        public ModelInputOutputDTO(int schemaVersion, Scenario scenario, DateTimeOffset generatedAt, Sources dataSources, Series dataSeries)
        {
            SchemaVersion = schemaVersion;
            Scenario = scenario;
            GeneratedAt = generatedAt;
            DataSources = dataSources;
            DataSeries = dataSeries;
        }

        /// <summary>Schema version of this artifact; see <see cref="ArtifactSchemaVersions.OperationalDemand"/>.</summary>
        [JsonPropertyName("schemaVersion")]
        [Required]
        public int SchemaVersion { get; set; }
        /// <summary>The region and period the demand series covers.</summary>
        [JsonPropertyName("scenario")]
        [Required]
        public Scenario Scenario { get; set; }
        /// <summary>When this artifact was generated, in UTC.</summary>
        [JsonPropertyName("generatedAt")]
        [Required]
        public DateTimeOffset GeneratedAt { get; set; }
        /// <summary>The upstream archive filenames the demand series was assembled from.</summary>
        [JsonPropertyName("dataSources")]
        [Required]
        public Sources DataSources { get; set; }
        /// <summary>The demand series itself.</summary>
        [JsonPropertyName("dataSeries")]
        [Required]
        public Series DataSeries { get; set; }
    }

    /// <summary>Upstream archive provenance for an operational demand artifact.</summary>
    public struct Sources(string[] demandSourceFiles)
    {
        /// <summary>
        /// Descriptive filenames of the upstream demand archives the series was assembled from.
        /// This is provenance only, not a digest; it does not replace the demand artifact's own
        /// SHA-256 recorded elsewhere.
        /// </summary>
        public string[] DemandSourceFiles { get; set; } = demandSourceFiles;
    }

    /// <summary>The demand data carried by an operational demand artifact.</summary>
    public struct Series(double[] demandMw)
    {
        // TODO: DTO carries bare doubles (MW); keep domain value objects out of the contract so the results.json shape can version independently.
        /// <summary>
        /// Interval demand in MW, one value per interval of <see cref="Scenario.Resolution"/>
        /// starting at <see cref="Scenario.PeriodStart"/>. Interval-average MW; integrates to MWh.
        /// </summary>
        public double[] DemandMw { get; set; } = demandMw;
    }

    /// <summary>
    /// The region and period an operational demand artifact covers. Despite the name this is not
    /// the <c>NEM.Model</c> scenario domain type; it is this artifact's own, narrower descriptor.
    /// </summary>
    public struct Scenario(string id, string region, DateTimeOffset periodStart, DateTimeOffset periodEnd, TimeSpan resolution, string aggregation)
    {
        /// <summary>Identifier for this demand extract, derived from the region name.</summary>
        public string Id { get; set; } = id;
        /// <summary>The NEM region this demand series belongs to.</summary>
        public string Region { get; set; } = region;
        /// <summary>Start of the covered period, in NEM market time (UTC+10).</summary>
        public DateTimeOffset PeriodStart { get; set; } = periodStart;
        /// <summary>End of the covered period, in NEM market time (UTC+10).</summary>
        public DateTimeOffset PeriodEnd { get; set; } = periodEnd;
        /// <summary>Interval length of <see cref="Series.DemandMw"/>.</summary>
        public TimeSpan Resolution { get; set; } = resolution;
        /// <summary>
        /// Free-text description of how the series was aggregated, e.g. whether it spans more than
        /// one region and how overlapping source records were reconciled.
        /// </summary>
        public string Aggregation { get; set; } = aggregation;
    }
}
