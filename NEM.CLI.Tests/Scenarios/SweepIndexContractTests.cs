using System.Text.Json;
using FluentAssertions;
using NEM.Contracts;

namespace NEM.CLI.Tests.Scenarios;

public sealed class SweepIndexContractTests
{
    [Fact]
    public void V2_RoundTripsWithExplicitUnitsAndUnavailableAchievedShares()
    {
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        var weatherBasis = new WeatherBasisDTO(
            WeatherBasisKind.TypicalMeteorologicalYear,
            "sydney.epw",
            "Sydney (WMO 947680)",
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
                new Dictionary<string, int> { ["dispatchResults"] = 4 }),
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
                    null,
                    null,
                    100,
                    400,
                    0,
                    0,
                    0,
                    1,
                    0,
                    10),
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
                null)]);

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
        json.Should().Contain("\"achievedRenewableShareGridScale\":null");
        json.Should().Contain("\"achievedRenewableShareNative\":null");
        json.Should().Contain("\"status\":\"succeeded\"");
        json.Should().Contain("\"regionIds\"");
        json.Should().Contain("\"weatherBasis\"");
        json.Should().Contain("\"outcome\":\"notRequired\"");
        json.Should().Contain("\"unservedHours\"");
        json.Should().Contain("\"peakUnservedPowerMw\"");
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