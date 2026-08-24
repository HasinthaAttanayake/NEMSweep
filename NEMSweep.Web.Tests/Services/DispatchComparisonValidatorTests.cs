using AwesomeAssertions;
using NEMSweep.Contracts;
using NEMSweep.Web.Services;

namespace NEMSweep.Web.Tests.Services;

public sealed class DispatchComparisonValidatorTests
{
    [Fact]
    public void Validate_AcceptsTwoRunsOverTheSamePeriodAndResolution()
    {
        DispatchComparisonValidator.Validate(ArtifactFixtures.Results(), ArtifactFixtures.Results())
            .Should().BeNull();
    }

    [Fact]
    public void Validate_RejectsADifferentPeriod()
    {
        DispatchResultsDTO comparison = ArtifactFixtures.Results(scenario:
            ArtifactFixtures.Scenario() with { PeriodStart = ArtifactFixtures.PeriodStart.AddDays(1) });

        DispatchComparisonValidator.Validate(ArtifactFixtures.Results(), comparison)
            .Should().Contain("different period");
    }

    [Fact]
    public void Validate_RejectsADifferentResolution()
    {
        DispatchResultsDTO comparison = ArtifactFixtures.Results(scenario:
            ArtifactFixtures.Scenario() with { Resolution = TimeSpan.FromMinutes(30) });

        DispatchComparisonValidator.Validate(ArtifactFixtures.Results(), comparison)
            .Should().Contain("different resolution");
    }

    [Fact]
    public void Validate_RejectsADifferentRegion()
    {
        DispatchResultsDTO comparison = ArtifactFixtures.Results(scenario:
            ArtifactFixtures.Scenario() with { Region = "VIC1" });

        DispatchComparisonValidator.Validate(ArtifactFixtures.Results(), comparison)
            .Should().Contain("different region");
    }

    [Fact]
    public void Validate_RejectsADifferentIntervalCount()
    {
        DispatchComparisonValidator.Validate(
            ArtifactFixtures.Results(),
            ArtifactFixtures.Results(intervals: 5) with { Scenario = ArtifactFixtures.Scenario() })
            .Should().Contain("different number of intervals");
    }
}
