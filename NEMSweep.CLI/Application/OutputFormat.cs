namespace NEMSweep.CLI.Application;

/// <summary>How a command reports its result.</summary>
internal enum OutputFormat
{
    /// <summary>Prose for a person reading a terminal. The default.</summary>
    Text,

    /// <summary>One JSON object, for a caller that acts on the answer rather than reading it.</summary>
    Json,
}
