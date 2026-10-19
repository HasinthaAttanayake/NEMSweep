using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace NEMSweep.CLI.Tests.Infrastructure;

/// <summary>
/// Keeps every install snippet in the repository's Markdown pinned to the documentation's release target
/// recorded in <c>Directory.Build.props</c>. The versions in these snippets are what a reader, or
/// an agent reading the raw Markdown, actually types: a stale one sends them to a package that does
/// not have the commands the surrounding prose describes, and nothing else in the build notices.
/// </summary>
public sealed class DocumentedVersionTests
{
    /// <summary>
    /// A line carrying this marker, or the line before it, is exempt. It is for a snippet that
    /// deliberately records history, so the exemption has to be written down next to the snippet
    /// rather than inferred from the prose around it.
    /// </summary>
    private const string HistoricalMarker = "docs-version-check: historical";

    /// <summary>Matches a `dotnet` command that pins one of the NEMSweep packages to a version.</summary>
    private static readonly Regex PinnedInstall = new(
        @"NEMSweep\.(?:CLI|Model|Contracts)\s+--version\s+(?<version>[0-9][^\s`]*)",
        RegexOptions.Compiled);

    [Fact]
    public void EveryDocumentedInstallPinsTheDocumentedVersion()
    {
        string root = RepositoryRoot();
        string expected = DocumentedCliVersion(root);

        List<string> drifted = [];
        foreach (string file in MarkdownFiles(root))
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                if (IsExempt(lines, index))
                {
                    continue;
                }

                foreach (Match match in PinnedInstall.Matches(lines[index]))
                {
                    string version = match.Groups["version"].Value;
                    if (!string.Equals(version, expected, StringComparison.Ordinal))
                    {
                        drifted.Add(
                            $"{Path.GetRelativePath(root, file)}:{index + 1} pins {version}");
                    }
                }
            }
        }

        drifted.Should().BeEmpty(
            $"every install snippet must pin the documented version {expected} from "
            + "Directory.Build.props, or carry the "
            + $"'{HistoricalMarker}' marker if it deliberately records history");
    }

    [Fact]
    public void AtLeastOneInstallSnippetIsChecked()
    {
        // Without this the test above passes vacuously if the snippets are ever reworded out of the
        // shape the pattern recognises.
        string root = RepositoryRoot();
        int found = MarkdownFiles(root)
            .SelectMany(File.ReadAllLines)
            .Sum(line => PinnedInstall.Matches(line).Count);

        found.Should().BeGreaterThan(0);
    }

    private static bool IsExempt(string[] lines, int index) =>
        lines[index].Contains(HistoricalMarker, StringComparison.Ordinal)
        || (index > 0 && lines[index - 1].Contains(HistoricalMarker, StringComparison.Ordinal));

    private static IEnumerable<string> MarkdownFiles(string root) =>
        new[] { "README.md", "CONTRIBUTING.md", "AGENTS.md" }
            .Select(name => Path.Combine(root, name))
            .Where(File.Exists)
            .Concat(EnumerateMarkdown(Path.Combine(root, "docs")))
            .Concat(EnumerateMarkdown(Path.Combine(root, "skills")));

    private static IEnumerable<string> EnumerateMarkdown(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_site{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}api{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            : [];

    /// <summary>
    /// Reads the documented version out of <c>Directory.Build.props</c> as text rather than through
    /// MSBuild, so the test depends on the same file a reader would edit and on nothing else.
    /// </summary>
    private static string DocumentedCliVersion(string root)
    {
        string props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        Match match = Regex.Match(
            props,
            @"<DocumentedCliVersion>(?<version>[^<]+)</DocumentedCliVersion>");
        match.Success.Should().BeTrue(
            "Directory.Build.props must declare DocumentedCliVersion");
        return match.Groups["version"].Value.Trim();
    }

    /// <summary>Walks up from the test assembly to the directory holding the solution file.</summary>
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NEMSweep.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the repository");
        return directory!.FullName;
    }
}
