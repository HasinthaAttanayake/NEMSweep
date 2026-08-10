using AwesomeAssertions;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

public sealed class DispatchDemandDecompositionValidatorTests
{
    [Fact]
    public void IsWithinRoundingEnvelope_AcceptsIndependentlyRoundedBaseAndComponent()
    {
        double serializedBaseDemandMw = Math.Round(0.06, 1, MidpointRounding.AwayFromZero);
        double serializedComponentDemandMw = Math.Round(0.06, 1, MidpointRounding.AwayFromZero);
        double serializedTotalDemandMw = Math.Round(0.06 + 0.06, 1, MidpointRounding.AwayFromZero);

        bool isValid = DispatchDemandDecompositionValidator.IsWithinRoundingEnvelope(
            serializedBaseDemandMw,
            [serializedComponentDemandMw],
            serializedTotalDemandMw);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void IsWithinRoundingEnvelope_RejectsMismatchBeyondQuantizationEnvelope()
    {
        bool isValid = DispatchDemandDecompositionValidator.IsWithinRoundingEnvelope(
            0.1,
            [0.1],
            0.4);

        isValid.Should().BeFalse();
    }
}