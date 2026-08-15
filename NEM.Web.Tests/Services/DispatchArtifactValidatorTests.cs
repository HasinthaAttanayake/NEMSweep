using AwesomeAssertions;
using NEM.Contracts;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

public sealed class DispatchArtifactValidatorTests
{
    [Fact]
    public void Validate_SystemWithAlignedDirectedEvidenceAndLossLedger_Accepts()
    {
        SystemDispatchResultsDTO result = SystemWithLink();

        DispatchArtifactValidator.Validate(result).Should().BeNull();
    }

    [Fact]
    public void Validate_SystemWithNegativeDirectedFlow_RejectsEvidence()
    {
        SystemDispatchResultsDTO result = SystemWithLink() with
        {
            Interconnectors =
            [new DispatchInterconnectorDTO("NSW1->VIC1", "NSW1", "VIC1", 100, [-1, 0, 0], [0, 0, 0])],
        };

        DispatchArtifactValidator.Validate(result)
            .Should().Be("System dispatch interconnector series must be finite and non-negative.");
    }

    [Fact]
    public void Validate_SystemWithLossLedgerMismatch_RejectsEvidence()
    {
        SystemDispatchResultsDTO result = SystemWithLink() with
        {
            DataSeries = ArtifactFixtures.Series([0, 0, 0], [0, 0, 0], []) with
            {
                TransmissionLossesMw = [2, 0, 0],
            },
        };

        DispatchArtifactValidator.Validate(result)
            .Should().Be("System transmission-loss series does not match its interconnector loss ledger.");
    }

    [Fact]
    public void Validate_SystemWithReciprocalDirectedEvidence_Accepts()
    {
        SystemDispatchResultsDTO result = SystemWithLink() with
        {
            Topology = new DispatchTopologyDTO(
                ["NSW1", "VIC1"],
                [
                    new DispatchTopologyLinkDTO("NSW1->VIC1", "NSW1", "VIC1", 100),
                    new DispatchTopologyLinkDTO("VIC1->NSW1", "VIC1", "NSW1", 100),
                ]),
            Interconnectors =
            [
                SystemWithLink().Interconnectors.Single(),
                new DispatchInterconnectorDTO("VIC1->NSW1", "VIC1", "NSW1", 100, [0, 0, 0], [0, 0, 0]),
            ],
        };

        DispatchArtifactValidator.Validate(result).Should().BeNull();
    }

    [Fact]
    public void Validate_SystemWithDuplicateDirectedEvidence_RejectsEvidence()
    {
        DispatchInterconnectorDTO link = SystemWithLink().Interconnectors.Single();
        SystemDispatchResultsDTO result = SystemWithLink() with { Interconnectors = [link, link] };

        DispatchArtifactValidator.Validate(result)
            .Should().Be("System dispatch interconnector evidence contains a duplicate link.");
    }

    [Fact]
    public void Validate_SystemWithFlowAboveDirectedCapacity_RejectsEvidence()
    {
        SystemDispatchResultsDTO result = SystemWithLink() with
        {
            Topology = new DispatchTopologyDTO(
                ["NSW1", "VIC1"],
                [new DispatchTopologyLinkDTO("NSW1->VIC1", "NSW1", "VIC1", 10)]),
            Interconnectors =
            [new DispatchInterconnectorDTO("NSW1->VIC1", "NSW1", "VIC1", 10, [10.1, 0, 0], [0.5, 0.25, 0])],
        };

        DispatchArtifactValidator.Validate(result)
            .Should().Be("System dispatch interconnector flow exceeds its declared capacity or loss ledger.");
    }

    [Fact]
    public void Validate_SystemWithEvidenceOutsideDeclaredTopology_RejectsEvidence()
    {
        SystemDispatchResultsDTO result = SystemWithLink() with
        {
            Interconnectors =
            [new DispatchInterconnectorDTO("VIC1->NSW1", "VIC1", "NSW1", 100, [0, 0, 0], [0, 0, 0])],
        };

        DispatchArtifactValidator.Validate(result)
            .Should().Be("System dispatch interconnector evidence does not match declared topology.");
    }

    [Fact]
    public void Validate_SystemWithNonReconcilingGenerationCostContributions_RejectsEvidence()
    {
        SystemDispatchResultsDTO result = SystemWithLink() with
        {
            Cost = ArtifactFixtures.SystemResults().Cost with
            {
                AnnualisedGenerationCostAud = 10m,
                GenerationCostContributions =
                [new DispatchGenerationCostContributionDTO("Solar", 9m, 1m)],
            },
        };

        DispatchArtifactValidator.Validate(result)
            .Should().Be("Dispatch generation cost contributions do not reconcile to annualised generation cost.");
    }

    [Fact]
    public void Validate_OverviewMatchingSystemEvidence_Accepts()
    {
        SystemDispatchOverviewDTO overview = OverviewFor(SystemWithLink());

        DispatchArtifactValidator.Validate((object)overview).Should().BeNull();
    }

    [Fact]
    public void Validate_OverviewWithNonReconcilingGenerationCostContributions_RejectsEvidence()
    {
        SystemDispatchOverviewDTO overview = OverviewFor(SystemWithLink()) with
        {
            Cost = ArtifactFixtures.SystemResults().Cost with
            {
                AnnualisedGenerationCostAud = 10m,
                GenerationCostContributions =
                [new DispatchGenerationCostContributionDTO("Solar", 9m, 1m)],
            },
        };

        DispatchArtifactValidator.Validate(overview)
            .Should().Be("Dispatch generation cost contributions do not reconcile to annualised generation cost.");
    }

    [Fact]
    public void Validate_OverviewWithDuplicateTopologyLink_RejectsEvidence()
    {
        DispatchTopologyLinkDTO duplicateLink = new("NSW1->VIC1", "NSW1", "VIC1", 100);
        SystemDispatchOverviewDTO overview = OverviewFor(SystemWithLink()) with
        {
            Topology = new DispatchTopologyDTO(["NSW1", "VIC1"], [duplicateLink, duplicateLink]),
        };

        DispatchArtifactValidator.Validate(overview)
            .Should().Be("System dispatch topology links are invalid.");
    }

    private static SystemDispatchOverviewDTO OverviewFor(SystemDispatchResultsDTO system) => new(
        system.SchemaVersion,
        system.RunId,
        system.PeriodStart,
        system.PeriodEnd,
        system.Resolution,
        system.RegionIds,
        system.DataSourcesByRegion,
        system.RegionSummariesById,
        system.Metrics,
        system.Reliability,
        system.StorageSizing,
        system.Cost,
        system.Topology);

    private static SystemDispatchResultsDTO SystemWithLink()
    {
        DispatchInterconnectorDTO link = new(
            "NSW1->VIC1",
            "NSW1",
            "VIC1",
            100,
            [10, 5, 0],
            [0.5, 0.25, 0]);
        SystemDispatchResultsDTO baseResult = ArtifactFixtures.SystemResults(interconnectors: [link]);
        return baseResult with
        {
            RegionIds = ["NSW1", "VIC1"],
                Topology = new DispatchTopologyDTO(
                ["NSW1", "VIC1"],
                [new DispatchTopologyLinkDTO("NSW1->VIC1", "NSW1", "VIC1", 100)]),
                RegionSummariesById = new Dictionary<string, RegionDispatchSummaryDTO>
                {
                    ["NSW1"] = baseResult.RegionSummariesById["NSW1"],
                    ["VIC1"] = baseResult.RegionSummariesById["NSW1"],
                },
            DataSeries = baseResult.DataSeries with { TransmissionLossesMw = [0.5, 0.25, 0] },
        };
    }
}