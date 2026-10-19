namespace NEMSweep.CLI.Application;

/// <summary>
/// Prints the agent guide embedded at build time.
/// </summary>
/// <remarks>
/// The guide is the repository's <c>skills/nemsweep/SKILL.md</c>, compiled into the assembly rather
/// than fetched. An agent that reads the copy on <c>main</c> is reading about a version it may not
/// have installed: the install commands, the flags and the field names it describes all move. The
/// embedded copy can only ever describe the binary printing it.
/// </remarks>
internal static class AgentGuideCommand
{
    /// <summary>Logical name the guide is embedded under; see <c>NEMSweep.CLI.csproj</c>.</summary>
    private const string ResourceName = "Agent.Guide";

    /// <summary>Writes the guide to the given writer.</summary>
    /// <param name="output">Where to write.</param>
    /// <returns>Zero: asking for the guide is a success.</returns>
    public static int Run(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write(Read());
        return 0;
    }

    /// <summary>Reads the embedded guide.</summary>
    /// <exception cref="InvalidOperationException">
    /// The resource is missing, which means the build dropped it rather than that the caller did
    /// anything wrong. Worth failing loudly: silently printing nothing would read as a guide with
    /// no content.
    /// </exception>
    internal static string Read()
    {
        using Stream stream = typeof(AgentGuideCommand).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The agent guide was not embedded in this build under '{ResourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
