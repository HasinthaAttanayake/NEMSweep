using System.Reflection;

namespace NEMSweep.CLI.Infrastructure;

/// <summary>
/// The commit this binary was built from, stamped into the assembly by the build rather than read
/// from git when a command runs. A published result names the model that produced it, and the model
/// is the binary: asking git at run time would answer for whichever repository the process was
/// launched inside, which for an installed or containerised CLI is an unrelated one.
/// </summary>
internal static class BuildProvenance
{
    private const string CommitShaKey = "BuildCommitSha";

    /// <summary>
    /// Commit the CLI was built from, or <see langword="null"/> when it was built outside a
    /// checkout, which is the normal case for a source archive.
    /// </summary>
    public static string? CommitSha { get; } = ReadCommitSha();

    private static string? ReadCommitSha()
    {
        string? value = typeof(BuildProvenance).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == CommitShaKey)
            ?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
