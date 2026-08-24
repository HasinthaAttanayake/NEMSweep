using AwesomeAssertions;
using NEMSweep.CLI.Demand;
using NEMSweep.Contracts;
using NEMSweep.Model.Series;
using System.Text.Json;

namespace NEMSweep.CLI.Tests.Demand;

public sealed class OperationalDemandExportTests
{
    [Fact]
    public void Create_RoundTripsSeriesAndEndExclusivePeriodThroughJson()
    {
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        var demand = new OperationalDemandData(
            "NSW1",
            new FlowSeries(start, TimeSpan.FromMinutes(30), [8_000, 8_100]),
            ["first.zip", "second.zip"]);

        ModelInputOutputDTO export = OperationalDemandExport.Create(demand);
        string json = JsonSerializer.Serialize(export);
        ModelInputOutputDTO? roundTripped = JsonSerializer.Deserialize<ModelInputOutputDTO>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.SchemaVersion.Should().Be(2);
        roundTripped.Scenario.Region.Should().Be("NSW1");
        roundTripped.Scenario.PeriodStart.Should().Be(start);
        roundTripped.Scenario.PeriodEnd.Should().Be(start.AddHours(1));
        roundTripped.Scenario.Resolution.Should().Be(TimeSpan.FromMinutes(30));
        roundTripped.Scenario.Aggregation.Should().Be(
            "single region; no cross-region aggregation; identical overlaps deduplicated");
        roundTripped.DataSources.DemandSourceFiles.Should().Equal("first.zip", "second.zip");
        roundTripped.DataSeries.DemandMw.Should().Equal(8_000, 8_100);
    }
}