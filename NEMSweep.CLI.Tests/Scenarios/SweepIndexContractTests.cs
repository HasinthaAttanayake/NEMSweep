using System.Text.Json;
using AwesomeAssertions;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Tests.Scenarios;

public sealed class SweepIndexContractTests
{
    [Fact]
    public void V6_RoundTripsWithTransmissionScalarsAndExplicitUnits()
    {
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        var weatherBasis = new WeatherBasisDTO(
            WeatherBasisKind.TypicalMeteorologicalYear,
            new WeatherSiteDTO("sydney-solar.epw", "Sydney (WMO 947680)"),
            new WeatherSiteDTO("sydney-wind.epw", "Sydney (WMO 947680)"),
            "Typical meteorological year from sydney.epw.");
        var index = new SweepIndexDTO(
            ArtifactSchemaVersions.SweepIndex,
            "test-sweep",
            "Test sweep",
            new SweepAxisDTO("Capacity", "MW"),
            new SweepScopeDTO(
                ["NSW1"],
                start,
                start.AddYears(1),
                TimeSpan.FromHours(1),
                weatherBasis),
            new SweepProvenanceDTO(
                "abc123",
                true,
                new string('a', 64),
                [new SweepInputFileDTO("demand.json", "demand-data", new string('b', 64))],
                new Dictionary<string, int> { ["dispatchResults"] = ArtifactSchemaVersions.DispatchResults }),
            [new SweepIndexPointDTO(
                "p0",
                "Baseline",
                0,
                SweepPointStatus.Succeeded,
                "points/p0.json",
                "configs/p0.json",
                new SweepPointScalarResultsDTO(
                    100m,
                    80m,
                    20m,
                    87_600,
                    87_600,
                    90_000,
                    0.7,
                    0.6,
                    100,
                    400,
                    0,
                    0,
                    0,
                    1,
                    0,
                    10,
                    2m,
                    TransmissionCostStatus.Calculated,
                    -100),
                new ReliabilityBasisDTO(0.002, 0, true, "NEM reliability standard"),
                new StorageSizingOutcomeDTO(
                    StorageSizingOutcome.NotRequired,
                    400,
                    100,
                    400,
                    100,
                    100_000,
                    10_000,
                    1),
                new IntervalPointersDTO(null, 5, 12),
                null,
                [new SweepPointRegionScalarsDTO(
                    "NSW1",
                    new SweepPointScalarResultsDTO(
                        90m,
                        70m,
                        20m,
                        87_600,
                        87_600,
                        90_000,
                        0.7,
                        0.6,
                        100,
                        400,
                        0,
                        0,
                        0,
                        1,
                        0,
                        10,
                        0m,
                        TransmissionCostStatus.NotModelled,
                        100))],
                [new SweepPointRegionDetailDTO("NSW1", "points/p0-nsw1.json", "points/p0-nsw1-overview.json")],
                "points/p0-overview.json")]);

        string json = JsonSerializer.Serialize(index, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        SweepIndexDTO? roundTripped = JsonSerializer.Deserialize<SweepIndexDTO>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        roundTripped.Should().BeEquivalentTo(index);
        json.Should().Contain("\"slcoeAudPerMwh\"");
        json.Should().Contain("\"storagePowerMw\"");
        json.Should().Contain("\"storageEnergyMwh\"");
        json.Should().Contain("\"achievedRenewableShareGridScale\":0.7");
        json.Should().Contain("\"achievedRenewableShareNative\":0.6");
        json.Should().Contain("\"status\":\"succeeded\"");
        json.Should().Contain("\"regionIds\"");
        json.Should().Contain("\"regionScalars\"");
        json.Should().Contain("\"regionDetails\"");
        json.Should().Contain("\"overviewPath\":\"points/p0-overview.json\"");
        json.Should().Contain("\"weatherBasis\"");
        json.Should().Contain("\"solar\"");
        json.Should().Contain("\"wind\"");
        json.Should().Contain("\"outcome\":\"notRequired\"");
        json.Should().Contain("\"unservedHours\"");
        json.Should().Contain("\"peakUnservedPowerMw\"");
        json.Should().Contain("\"transmissionSlcotAudPerMwh\"");
        json.Should().Contain("\"transmissionCostStatus\":\"calculated\"");
        json.Should().Contain("\"netImportedEnergyMwh\"");
    }

    [Fact]
    public void FailedPoint_CarriesAStageAndCodeRatherThanFreeText()
    {
        var failure = new SweepPointFailureDTO(
            SweepFailureStage.Sizing,
            "batteryCapacityLimitReached",
            "Storage sizing ended with BatteryCapacityLimitReached.");

        string json = JsonSerializer.Serialize(failure, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        json.Should().Contain("\"stage\":\"sizing\"");
        json.Should().Contain("\"code\":\"batteryCapacityLimitReached\"");
        JsonSerializer.Deserialize<SweepPointFailureDTO>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            .Should().Be(failure);
    }

    [Fact]
    public void ScalarCatalog_DescribesEveryScalarExactlyOnce()
    {
        SweepScalarCatalog.Descriptors.Select(descriptor => descriptor.Name)
            .Should().BeEquivalentTo(SweepScalarCatalog.ScalarNames());
        SweepScalarCatalog.Descriptors.Should().OnlyContain(descriptor =>
            !string.IsNullOrWhiteSpace(descriptor.Label)
            && !string.IsNullOrWhiteSpace(descriptor.Unit));
        SweepScalarCatalog.Find("slcoeAudPerMwh")!.Currency.Should().Be("AUD");
        SweepScalarCatalog.Find("notAScalar").Should().BeNull();
    }
}