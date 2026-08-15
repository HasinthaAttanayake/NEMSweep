using AwesomeAssertions;
using NEM.Contracts;
using NEM.Web.Services.Insights;

namespace NEM.Web.Tests.Services;

public sealed class SweepMeasuresTests
{
    [Fact]
    public void For_OffersTheSweepsOwnInputAlongsideEveryPublishedScalar()
    {
        IReadOnlyList<SweepMeasure> measures = SweepMeasures.For(Index());

        measures[0].Key.Should().Be(SweepMeasures.AxisKey);
        measures[0].Label.Should().Be("Added demand");
        measures[0].Unit.Should().Be("MW");
        measures.Select(measure => measure.Key).Should().Contain(SweepScalarCatalog.Descriptors
            .Select(descriptor => descriptor.Name));
    }

    [Fact]
    public void For_MarksTheAnnualCostAsDerivedBecauseTheArtifactPublishesItsFactorsNotItsProduct()
    {
        SweepMeasure measure = SweepMeasures.Resolve(SweepMeasures.For(Index()), SweepMeasures.TotalCostKey);

        measure.IsDerived.Should().BeTrue();
        measure.Prefix.Should().Be("$");
    }

    [Fact]
    public void Resolve_FallsBackToTheFirstMeasureForAnUnknownKey()
    {
        IReadOnlyList<SweepMeasure> measures = SweepMeasures.For(Index());

        SweepMeasures.Resolve(measures, "not-a-measure").Should().BeSameAs(measures[0]);
    }

    [Fact]
    public void Varies_IsFalseForAMeasureThatHoldsStillAcrossTheSweep()
    {
        IReadOnlyList<SweepMeasure> measures = SweepMeasures.For(Index());
        IReadOnlyList<SweepRun> runs = Runs(0.38, 0.38);

        SweepMeasures.Varies(SweepMeasures.Resolve(measures, SweepMeasures.RenewableShareKey), runs)
            .Should().BeFalse();
    }

    [Fact]
    public void DefaultX_OpensOnRenewableShareWhenTheSweepMovedIt()
    {
        IReadOnlyList<SweepMeasure> measures = SweepMeasures.For(Index());

        SweepMeasures.DefaultX(measures, Runs(0.38, 0.46)).Key
            .Should().Be(SweepMeasures.RenewableShareKey);
    }

    [Fact]
    public void DefaultX_FallsBackToTheSweepsOwnInputWhenRenewableShareIsAbsent()
    {
        IReadOnlyList<SweepMeasure> measures = SweepMeasures.For(Index());

        SweepMeasures.DefaultX(measures, Runs(null, null)).Key.Should().Be(SweepMeasures.AxisKey);
    }

    private static SweepIndexDTO Index() =>
        ArtifactFixtures.Index(ArtifactFixtures.SucceededPoint("p0", "Baseline", 0));

    private static IReadOnlyList<SweepRun> Runs(params double?[] renewableShares) =>
        [.. renewableShares.Select((share, index) => new SweepRun(
            ArtifactFixtures.SucceededPoint($"p{index}", $"Run {index}", index),
            ArtifactFixtures.Scalars() with { AchievedRenewableShareGridScale = share }))];
}
