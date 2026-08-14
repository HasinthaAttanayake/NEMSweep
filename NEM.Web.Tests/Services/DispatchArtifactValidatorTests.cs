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
            [new DispatchInterconnectorDTO("NSW1", "VIC1", 100, [-1, 0, 0], [0, 0, 0])],
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
            Interconnectors =
            [
                SystemWithLink().Interconnectors.Single(),
                new DispatchInterconnectorDTO("VIC1", "NSW1", 100, [0, 0, 0], [0, 0, 0]),
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
            Interconnectors =
            [new DispatchInterconnectorDTO("NSW1", "VIC1", 10, [10.1, 0, 0], [0.5, 0.25, 0])],
        };

        DispatchArtifactValidator.Validate(result)
            .Should().Be("System dispatch interconnector flow exceeds its declared capacity or loss ledger.");
    }

    private static SystemDispatchResultsDTO SystemWithLink()
    {
        DispatchInterconnectorDTO link = new(
            "NSW1",
            "VIC1",
            100,
            [10, 5, 0],
            [0.5, 0.25, 0]);
        SystemDispatchResultsDTO baseResult = ArtifactFixtures.SystemResults(interconnectors: [link]);
        return baseResult with
        {
            RegionIds = ["NSW1", "VIC1"],
            DataSeries = baseResult.DataSeries with { TransmissionLossesMw = [0.5, 0.25, 0] },
        };
    }
}