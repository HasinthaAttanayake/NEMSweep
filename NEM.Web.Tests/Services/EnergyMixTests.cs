using AwesomeAssertions;
using NEM.Contracts;
using NEM.Web.Components.Viz;
using NEM.Web.Services.Insights;

namespace NEM.Web.Tests.Services;

public sealed class EnergyMixTests
{
    [Fact]
    public void From_IntegratesPowerIntoEnergyUsingTheIntervalLength()
    {
        EnergyMix mix = Mix(TimeSpan.FromHours(2), ("Wind", [100, 100, 100]));

        // Three two-hour intervals at 100 MW is 600 MWh, not 300.
        mix.TotalMwh.Should().Be(600);
        mix.ByTechnology.Single().EnergyMwh.Should().Be(600);
    }

    [Fact]
    public void From_OrdersTechnologiesForStackingRatherThanByDictionaryOrder()
    {
        EnergyMix mix = Mix(
            TimeSpan.FromHours(1),
            ("Gas", [1]),
            ("Solar", [1]),
            ("Coal", [1]),
            ("Wind", [1]),
            ("Hydro", [1]));

        mix.ByTechnology.Select(entry => entry.Technology)
            .Should().Equal("Solar", "Wind", "Hydro", "Coal", "Gas");
    }

    [Fact]
    public void From_KeepsATechnologyThePaletteHasNotMet()
    {
        EnergyMix mix = Mix(TimeSpan.FromHours(1), ("Wind", [1]), ("Geothermal", [2]));

        mix.ByTechnology.Select(entry => entry.Technology).Should().Equal("Wind", "Geothermal");
        mix.TotalMwh.Should().Be(3);
    }

    /// <summary>
    /// The published NSW1 baseline reports a grid-scale renewable share of 0.3798 against delivered
    /// energy that is 34.51% solar and wind and a further 3.47% hydro. Deriving the share without
    /// hydro reproduces the model's separate native share instead, and the site then states two
    /// different renewable shares for one run.
    /// </summary>
    [Fact]
    public void RenewableShare_MatchesTheModelsGridScaleShareByCountingHydro()
    {
        EnergyMix mix = Mix(
            TimeSpan.FromHours(1),
            ("Solar", [2308]),
            ("Wind", [1143]),
            ("Hydro", [347]),
            ("Coal", [5202]),
            ("Gas", [1000]));

        mix.RenewableShare.Should().BeApproximately(0.3798, 0.0001);
    }

    [Fact]
    public void Renewable_CountsHydroSoEveryPageStatesOneDefinition()
    {
        TechnologyPalette.Renewable.Should().BeEquivalentTo(["Solar", "Wind", "Hydro"]);
    }

    [Fact]
    public void From_ReturnsEmptyForASeriesWithNoGeneration()
    {
        EnergyMix.From(null, TimeSpan.FromHours(1)).Should().BeSameAs(EnergyMix.Empty);
        Mix(TimeSpan.Zero, ("Wind", [1])).TotalMwh.Should().Be(0);
    }

    [Fact]
    public void RenewableShare_IsZeroRatherThanUndefinedWhenNothingWasDelivered()
    {
        EnergyMix.Empty.RenewableShare.Should().Be(0);
    }

    [Fact]
    public void Segments_CarryThePaletteColourForEachTechnology()
    {
        IReadOnlyList<MixSegment> segments = Mix(TimeSpan.FromHours(1), ("Wind", [1])).Segments();

        segments.Single().Color.Should().Be(TechnologyPalette.ForGeneration("Wind"));
    }

    [Fact]
    public void FromTotals_OrdersAPublishedMixTheSameWayAnIntegratedOneIsOrdered()
    {
        EnergyMix totals = EnergyMix.FromTotals(new Dictionary<string, double>
        {
            ["Wind"] = 100,
            ["Coal"] = 300,
        });

        // The producer's key order is whatever the JSON happened to carry; the site's is the
        // palette's, so the same run reads the same way on every page.
        EnergyMix integrated = Mix(TimeSpan.FromHours(1), ("Coal", [300]), ("Wind", [100]));
        totals.ByTechnology.Select(entry => entry.Technology).Should()
            .Equal(integrated.ByTechnology.Select(entry => entry.Technology));
        totals.TotalMwh.Should().Be(400);
        totals.RenewableShare.Should().Be(0.25);
    }

    [Fact]
    public void FromTotals_IsEmptyForAMixTheArtifactDoesNotCarry()
    {
        EnergyMix.FromTotals(null).Should().BeSameAs(EnergyMix.Empty);
        EnergyMix.FromTotals(new Dictionary<string, double>()).Should().BeSameAs(EnergyMix.Empty);
    }

    [Fact]
    public void Combine_AddsRegionalMixesIntoTheSystemTotal()
    {
        EnergyMix combined = EnergyMix.Combine(
        [
            EnergyMix.FromTotals(new Dictionary<string, double> { ["Wind"] = 100, ["Coal"] = 300 }),
            EnergyMix.FromTotals(new Dictionary<string, double> { ["Wind"] = 50, ["Solar"] = 50 }),
        ]);

        combined.TotalMwh.Should().Be(500);
        combined.ByTechnology.Single(entry => entry.Technology == "Wind").EnergyMwh.Should().Be(150);
        combined.ByTechnology.Single(entry => entry.Technology == "Solar").EnergyMwh.Should().Be(50);
    }

    private static EnergyMix Mix(TimeSpan resolution, params (string Technology, double[] Values)[] series)
    {
        DispatchSeriesDTO source = ArtifactFixtures.Results().DataSeries with
        {
            DeliveredGenerationByTechnologyMw = series.ToDictionary(
                entry => entry.Technology,
                entry => entry.Values),
        };
        return EnergyMix.From(source, resolution);
    }
}
