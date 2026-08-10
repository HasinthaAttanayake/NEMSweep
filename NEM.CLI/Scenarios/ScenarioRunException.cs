using NEM.Contracts;

namespace NEM.CLI.Scenarios;

/// <summary>
/// A scenario run failure that knows which stage it happened in. Sweep points carry the stage and
/// code through to the index so failures can be grouped without matching on message text.
/// </summary>
internal sealed class ScenarioRunException : Exception
{
    public ScenarioRunException(
        SweepFailureStage stage,
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Stage = stage;
        Code = code;
    }

    /// <summary>Stage of the run the failure happened in.</summary>
    public SweepFailureStage Stage { get; }

    /// <summary>Stable, machine-readable reason within the stage.</summary>
    public string Code { get; }
}
