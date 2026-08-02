using FluentAssertions;
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

    private static Region Region(string regionId, double demandMw) =>
        new(
            regionId,
            [new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(100))],
            new FlowSeries(NemStart, TimeSpan.FromHours(1), [demandMw]));
}
