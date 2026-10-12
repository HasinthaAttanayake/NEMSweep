using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>Whether dispatch uses the declared fleet or searches for additional Battery capacity.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SimulationExecutionMode>))]
public enum SimulationExecutionMode
{
    /// <summary>Dispatch the declared fleet unchanged; no sizing evidence is produced.</summary>
    [JsonStringEnumMemberName("fixedFleet")]
    FixedFleet,

    /// <summary>Run the bounded Battery sizing search before publishing final dispatch evidence.</summary>
    [JsonStringEnumMemberName("sizeBattery")]
    SizeBattery,
}
