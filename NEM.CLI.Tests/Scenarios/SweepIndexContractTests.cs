using System.Text.Json;
using FluentAssertions;
using NEM.Contracts;

namespace NEM.CLI.Tests.Scenarios;

public sealed class SweepIndexContractTests
{
    [Fact]
    public void V1_RoundTripsWithExplicitUnitsAndUnavailableAchievedShares()
    {
        var index = new SweepIndexDTO(
            1,
            "test-sweep",
            "Test sweep",
            new SweepAxisDTO("Capacity", "MW"),
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
                "succeeded",
                "points/p0.json",
                "configs/p0.json",
                new SweepPointScalarResultsDTO(
                    100m,
                    80m,
                    20m,
                    87_600,
                    null,
                    null,
                    100,
                    400,
                    0,
                    0,
                    10),
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
    }
}