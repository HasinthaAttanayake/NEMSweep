using AwesomeAssertions;
using NEMSweep.CLI.Application;

namespace NEMSweep.CLI.Tests.Application;

public sealed class AgentGuideCommandTests
{
    [Fact]
    public void Run_PrintsTheGuideAndSucceeds()
    {
        var output = new StringWriter();

        int exitCode = AgentGuideCommand.Run(output);

        exitCode.Should().Be(0);
        output.ToString().Should().NotBeEmpty();
    }

    /// <summary>
    /// The guide is an Agent Skill, so its front matter is what makes it loadable as one. A build
    /// that embedded some other file, or a truncated one, would still print something.
    /// </summary>
    [Fact]
    public void Run_PrintsTheAgentSkillWithItsFrontMatterIntact()
    {
        var output = new StringWriter();

        AgentGuideCommand.Run(output);

        string guide = output.ToString();
        guide.Should().StartWith("---");
        guide.Should().Contain("name: nemsweep");
        guide.Should().Contain("description:");
    }

    [Fact]
    public void Run_PrintsTheWholeGuideRatherThanItsOpeningSection()
    {
        var output = new StringWriter();

        AgentGuideCommand.Run(output);

        string guide = output.ToString();
        guide.Should().Contain("## Preflight checklist");
        guide.Should().Contain("## Reading results");
        guide.Should().Contain("## Reporting results");
    }

    /// <summary>
    /// The point of embedding rather than fetching is that the guide and the binary move together,
    /// so the embedded copy has to be the repository's own file and not a stale one an incremental
    /// build kept.
    /// </summary>
    [Fact]
    public void Guide_IsTheRepositoryFileAndNotAStaleCopy()
    {
        string onDisk = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "skills", "nemsweep", "SKILL.md"));

        AgentGuideCommand.Read().ReplaceLineEndings().Should().Be(onDisk.ReplaceLineEndings());
    }

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
