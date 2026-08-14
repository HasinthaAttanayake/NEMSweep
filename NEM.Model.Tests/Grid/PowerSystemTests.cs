using AwesomeAssertions;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Tests.Grid;

public sealed class PowerSystemTests
{
    private static readonly DateTimeOffset NemStart =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void WithRegions_PreservesIdentityAndLeavesSourceUnchanged()
    {
        Region originalRegion = Region("NSW1", 100);
        Region replacementRegion = Region("NSW1", 120);
        var system = new PowerSystem(
            new PowerSystemId("test-system"),
            new ScenarioId("test-scenario"),
            [originalRegion]);

        PowerSystem replaced = system.WithRegions([replacementRegion]);

        replaced.Id.Should().BeSameAs(system.Id);
        replaced.DerivedFromScenario.Should().BeSameAs(system.DerivedFromScenario);
        replaced.Regions.Should().ContainSingle().Which.Should().BeSameAs(replacementRegion);
        system.Regions.Should().ContainSingle().Which.Should().BeSameAs(originalRegion);
    }

    [Fact]
    public void Construction_DefaultsToNoInterconnectors()
    {
        var system = new PowerSystem(
            new PowerSystemId("test-system"),
            new ScenarioId("test-scenario"),
            [Region("NSW1", 100)]);

        system.Interconnectors.Should().BeEmpty();
    }

    [Fact]
    public void WithRegions_ForwardsInterconnectors()
    {
        Interconnector link = Link("NSW1", "VIC1", 700);
        PowerSystem system = System([Region("NSW1", 100), Region("VIC1", 100)], [link]);

        PowerSystem replaced = system.WithRegions([Region("NSW1", 120), Region("VIC1", 100)]);

        replaced.Interconnectors.Should().ContainSingle().Which.Should().BeSameAs(
            link,
            "storage sizing rebuilds regions repeatedly and must not silently drop links");
    }

    [Fact]
    public void WithInterconnectors_ReplacesLinksAndLeavesSourceUnchanged()
    {
        PowerSystem system = System([Region("NSW1", 100), Region("VIC1", 100)]);

        PowerSystem linked = system.WithInterconnectors([Link("NSW1", "VIC1", 700)]);

        linked.Interconnectors.Should().ContainSingle();
        system.Interconnectors.Should().BeEmpty();
    }

    [Fact]
    public void Construction_RejectsInterconnectorEndpointThatIsNotARegion()
    {
        var act = () => System([Region("NSW1", 100)], [Link("NSW1", "VIC1", 700)]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*VIC1*is not a region of this power system*");
    }

    [Fact]
    public void Construction_AllowsInterconnectorsInOppositeDirections()
    {
        PowerSystem system = System(
            [Region("NSW1", 100), Region("VIC1", 100)],
            [Link("NSW1", "VIC1", 700), Link("VIC1", "NSW1", 100)]);

        system.Interconnectors.Should().HaveCount(2);
    }

    [Fact]
    public void Construction_RejectsDuplicateInterconnectorsInTheSameDirection()
    {
        var act = () => System(
            [Region("NSW1", 100), Region("VIC1", 100)],
            [Link("NSW1", "VIC1", 700), Link("nsw1", "vic1", 100)]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*duplicate interconnectors from*");
    }

    private static PowerSystem System(
        IReadOnlyList<Region> regions,
        IReadOnlyList<Interconnector>? interconnectors = null) =>
        new(
            new PowerSystemId("test-system"),
            new ScenarioId("test-scenario"),
            regions,
            interconnectors);

    private static Interconnector Link(
        string fromRegionId,
        string toRegionId,
        double capacityMw) =>
        new(
            fromRegionId,
            toRegionId,
            Power.FromMegawatts(capacityMw));

    private static Region Region(string regionId, double demandMw) =>
        new(
            regionId,
            [new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(100))],
            new FlowSeries(NemStart, TimeSpan.FromHours(1), [demandMw]));
}
