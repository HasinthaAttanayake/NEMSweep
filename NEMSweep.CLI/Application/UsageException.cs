namespace NEMSweep.CLI.Application;

/// <summary>
/// A command line that could not be read, as distinct from a command that ran and failed. Carried
/// as its own type so <see cref="CommandRouter"/> can hold the documented split between the two
/// exit codes: an unusable command line returns <c>2</c> alongside the usage text, and only a real
/// failure returns <c>1</c>.
/// </summary>
internal sealed class UsageException : Exception
{
    /// <summary>Creates a usage failure carrying the text shown to the caller.</summary>
    /// <param name="message">What about the command line could not be read.</param>
    public UsageException(string message)
        : base(message)
    {
    }
}
