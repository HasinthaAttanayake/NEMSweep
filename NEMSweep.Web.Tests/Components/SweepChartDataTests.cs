using AwesomeAssertions;
using NEMSweep.Contracts;
using NEMSweep.Web.Components;

namespace NEMSweep.Web.Tests.Components;

public sealed class SweepChartDataTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(12)]
    public void Build_RendersEverySucceededPointForTheArtifactPointCount(int pointCount)
    {
        SweepIndexDTO index = ArtifactFixtures.Index(
            [.. Enumerable.Range(0, pointCount)
                .Select(i => ArtifactFixtures.SucceededPoint($"p{i}", $"+{i} MW", i, i * 100))]);

        SweepChartData chart = SweepChartData.Build(index, StorageEnergyAxis());

        chart.Labels.Should().HaveCount(pointCount);
        chart.Values.Should().HaveCount(pointCount);
        chart.Points.Select(point => point.Point.AxisValue).Should().Equal(
            Enumerable.Range(0, pointCount).Select(index => (double)index));
    }

    /// <summary>
    /// A sweep point records one reliability verdict and it is the system's, so every scope-aware
    /// view has to judge its own scope: a region that served all of its load inside a system that
    /// did not must not be marked as having missed the standard.
    /// </summary>
    [Fact]
    public void WithinReliabilityTarget_JudgesTheScopeRatherThanTheSystemVerdict()
    {
        ReliabilityBasisDTO systemMissed = new(0.002, 5.93, false, "NEM reliability standard");

        SweepChartData.WithinReliabilityTarget(systemMissed, Unserved(5.93)).Should().BeFalse();
        SweepChartData.WithinReliabilityTarget(systemMissed, Unserved(0)).Should().BeTrue();
        SweepChartData.WithinReliabilityTarget(systemMissed, Unserved(0.002)).Should().BeTrue();
    }

    [Fact]
    public void WithinReliabilityTarget_IsUnknownWithoutABasisOrScalarsForTheScope()
    {
        SweepChartData.WithinReliabilityTarget(null, Unserved(0)).Should().BeNull();
        SweepChartData.WithinReliabilityTarget(ArtifactFixtures.Reliability(), null).Should().BeNull();
    }

    /// <summary>A target of zero cannot be judged from a share, so the published verdict stands.</summary>
    [Fact]
    public void WithinReliabilityTarget_FallsBackToThePublishedVerdictWhenThereIsNoTarget()
    {
        ReliabilityBasisDTO noTarget = new(0, 0.5, false, "None");

        SweepChartData.WithinReliabilityTarget(noTarget, Unserved(0)).Should().BeFalse();
    }

    private static SweepPointScalarResultsDTO Unserved(double percentageOfDemand) =>
        ArtifactFixtures.Scalars() with { UnservedEnergyPercentageOfDemand = percentageOfDemand };

    [Fact]
    public void Build_ExcludesAConstrainedPointAndKeepsItsStageAndCode()
    {
        SweepIndexDTO index = ArtifactFixtures.Index(
            ArtifactFixtures.SucceededPoint("p0", "Baseline", 0, 100),
            ArtifactFixtures.FailedPoint("p1", "+1 MW", 1, "Bounds are insufficient."),
            ArtifactFixtures.SucceededPoint("p2", "+2 MW", 2, 300));

        SweepChartData chart = SweepChartData.Build(index, StorageEnergyAxis());

        chart.Values.Should().Equal(100, 300);
        chart.Labels.Should().Equal("0", "2");
        chart.OmittedPoints.Should().ContainSingle().Which.Should().Be(new SweepChartOmittedPoint(
            "+1 MW",
            1,
            "Bounds are insufficient.",
            SweepFailureStage.Sizing,
            "batteryCapacityLimitReached"));
    }

    [Fact]
    public void Build_UsesTheAchievedScalarRatherThanTheSweepAxisValue()
    {
        SweepIndexDTO index = ArtifactFixtures.Index(
            ArtifactFixtures.SucceededPoint("p0", "+500 MW", 500, 2750));

        SweepChartData chart = SweepChartData.Build(index, StorageEnergyAxis());

        chart.Values.Should().Equal(2750);
        chart.Points.Single().Point.AxisValue.Should().Be(500);
    }

    [Fact]
    public void Build_SurfacesASucceededPointTheArtifactCarriesNoValueFor()
    {
        SweepIndexDTO index = ArtifactFixtures.Index(
            ArtifactFixtures.SucceededPoint("p0", "Baseline", 0),
            ArtifactFixtures.SucceededPoint("p1", "+1 MW", 1) with
            {
                Scalars = ArtifactFixtures.Scalars(renewableShareNative: 0.5),
            });

        SweepChartData chart = SweepChartData.Build(
            index,
            SweepSeriesCatalogue.Resolve("achievedRenewableShareNative")!);

        chart.Values.Should().Equal(0.5);
        chart.OmittedPoints.Should().ContainSingle()
            .Which.Reason.Should().Contain("carries no");
    }

    [Fact]
    public void Build_UsesRegionalScalarsAndOmitsPointsWithoutThatRegion()
    {
        SweepIndexDTO index = ArtifactFixtures.Index(
            ArtifactFixtures.SucceededPoint("p0", "Baseline", 0) with
            {
                RegionScalars = [new SweepPointRegionScalarsDTO("VIC1", ArtifactFixtures.Scalars(200))],
            },
            ArtifactFixtures.SucceededPoint("p1", "+1 MW", 1));

        SweepChartData chart = SweepChartData.Build(
            index,
            SweepSeriesCatalogue.Resolve("storageEnergyMwh")!,
            "VIC1");

        chart.Values.Should().Equal(200);
        chart.OmittedPoints.Should().ContainSingle().Which.Label.Should().Be("+1 MW");
    }

    [Fact]
    public void SweepSeriesCatalogue_ReadsEveryScalarTheContractDeclares()
    {
        SweepSeriesCatalogue.UnmappedScalarNames.Should().BeEmpty(
            "every scalar in the contract needs an accessor before it can be charted");
        SweepSeriesCatalogue.All.Should().HaveCount(SweepScalarCatalog.Descriptors.Count(descriptor => descriptor.Chartable));
    }

    [Fact]
    public void SweepSeriesCatalogue_TakesItsLabelsAndUnitsFromTheContract()
    {
        foreach (SweepScalarDescriptor descriptor in SweepScalarCatalog.Descriptors.Where(descriptor => descriptor.Chartable))
        {
            SweepChartYAxis axis = SweepSeriesCatalogue.Resolve(descriptor.Name)!;

            axis.Label.Should().Be(descriptor.Label);
            axis.Unit.Should().Be(descriptor.Unit);
            axis.ValuePrefix.Should().Be(descriptor.Currency is null ? null : "$");
        }
    }

    [Fact]
    public void SweepSeriesCatalogue_DoesNotResolveAnUnknownSeries()
    {
        SweepSeriesCatalogue.Resolve("notASeries").Should().BeNull();
        SweepSeriesCatalogue.Resolve(null).Should().BeNull();
        SweepSeriesCatalogue.SupportedKeys.Should().Contain("storageEnergyMwh");
    }

    [Theory]
    [InlineData(170.55, "$170.55")]
    [InlineData(0, "$0")]
    [InlineData(-5.5, "-$5.50")]
    public void Short_PlacesTheCurrencySymbolInsideTheMinusSign(double value, string expected)
    {
        SweepValueFormat.Short(value, "$").Should().Be(expected);
    }

    [Fact]
    public void Short_LeavesAValueWithNoPrefixUnchanged()
    {
        SweepValueFormat.Short(170.55, null).Should().Be("170.55");
        SweepValueFormat.Short(170.55, string.Empty).Should().Be("170.55");
    }

    [Theory]
    [InlineData("N2", new[] { 170.55, 163.2, 152.0, 147.85 })]
    [InlineData("N0", new[] { 0d, 500, 1000, 2000 })]
    [InlineData("N1", new[] { 0d, 0, 35, 703.5 })]
    [InlineData("N0", new double[0])]
    public void ColumnFormat_HoldsEnoughDecimalsForEveryValueInTheColumn(string expected, double[] values)
    {
        SweepValueFormat.ColumnFormat(values.Select(value => (double?)value)).Should().Be(expected);
    }

    [Fact]
    public void ColumnFormat_IgnoresAbsentValuesAndStopsAtItsCeiling()
    {
        SweepValueFormat.ColumnFormat([null, 1.5, null]).Should().Be("N1");
        SweepValueFormat.ColumnFormat([0.000000000123], maximumDecimals: 4).Should().Be("N4");
        SweepValueFormat.ColumnFormat([double.NaN, double.PositiveInfinity, 2]).Should().Be("N0");
    }

    [Theory]
    [InlineData(0d, 1)]
    [InlineData(3.19, 1)]
    [InlineData(703.5, 200)]
    [InlineData(170.55, 50)]
    [InlineData(598490.7, 200000)]
    public void TickInterval_ChoosesAWholeStepFromTheData(double maximum, int expected)
    {
        SweepChartScale.TickInterval(maximum).Should().Be(expected);
    }

    [Fact]
    public void ColumnFormat_KeepsTheDecimalsOfAFractionalSweepAxis()
    {
        // A 0.1-0.4 uplift axis rounded to whole numbers reported every point, and both sides of a
        // boundary, as "0".
        double[] axisValues = [0, 0.1, 0.2, 0.3, 0.4];

        string format = SweepValueFormat.ColumnFormat(axisValues.Select(value => (double?)value));

        format.Should().Be("N1");
        axisValues.Select(value => value.ToString(format))
            .Should().Equal("0.0", "0.1", "0.2", "0.3", "0.4");
    }

    private static SweepChartYAxis StorageEnergyAxis() =>
        SweepSeriesCatalogue.Resolve("storageEnergyMwh")!;
}
