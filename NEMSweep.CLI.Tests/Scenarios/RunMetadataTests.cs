using AwesomeAssertions;
using NEMSweep.CLI.Scenarios;

namespace NEMSweep.CLI.Tests.Scenarios;

/// <summary>
/// A published result claims whether the model was uncommitted when it ran, so the claim has to
/// mean what it says. Writing results is not a change to the model, and a regeneration necessarily
/// modifies its own output partway through.
/// </summary>
public sealed class RunMetadataTests
{
    // Rooted the way the platform roots things, rather than spelled out: a literal Windows path is
    // not fully qualified on Linux, and Path.GetFullPath rejects it as a base.
    private static readonly string Root = Path.GetFullPath("run-metadata-fixture");
    private static readonly string Output =
        Path.Combine(Root, "NEMSweep.Web", "wwwroot", "data");

    [Fact]
    public void HasSourceChanges_IgnoresTheOutputItIsCurrentlyWriting()
    {
        string status = string.Join(
            '\n',
            " M NEMSweep.Web/wwwroot/data/results.json",
            " M NEMSweep.Web/wwwroot/data/results-nsw1.json");

        SweepArtifactExport.HasSourceChanges(status, Root, Output).Should().BeFalse();
    }

    [Fact]
    public void HasSourceChanges_ReadsTheFirstLineEvenWhenItsLeadingSpaceWasTrimmedAway()
    {
        // The caller trims the whole output, which strips the leading pad from the first line only.
        // A fixed-column read mangles exactly that one path, and every regeneration reported dirty
        // because of it.
        string status = string.Join(
            '\n',
            "M NEMSweep.Web/wwwroot/data/results.json",
            " M NEMSweep.Web/wwwroot/data/results-nsw1.json");

        SweepArtifactExport.HasSourceChanges(status, Root, Output).Should().BeFalse();
    }

    [Fact]
    public void HasSourceChanges_ReportsAChangeToTheModelItself()
    {
        string status = string.Join(
            '\n',
            " M NEMSweep.Web/wwwroot/data/results.json",
            " M NEMSweep.Model/Grid/Region.cs");

        SweepArtifactExport.HasSourceChanges(status, Root, Output).Should().BeTrue();
    }

    [Theory]
    [InlineData("?? scenarios/untracked.json")]
    [InlineData("A  scenarios/added.json")]
    [InlineData("R  scenarios/old.json -> scenarios/new.json")]
    public void HasSourceChanges_ReadsEveryStatusShapeGitEmits(string line)
    {
        SweepArtifactExport.HasSourceChanges(line, Root, Output).Should().BeTrue();
    }

    [Fact]
    public void HasSourceChanges_TreatsACleanTreeAsClean()
    {
        SweepArtifactExport.HasSourceChanges(string.Empty, Root, Output).Should().BeFalse();
        SweepArtifactExport.HasSourceChanges(null, Root, Output).Should().BeFalse();
    }

    [Fact]
    public void HasSourceChanges_ReportsDirtyWhenItDoesNotKnowWhereTheRunWrites()
    {
        // No output root means nothing can be excluded, so the honest answer is the pessimistic one.
        SweepArtifactExport.HasSourceChanges(" M anything.cs", Root, outputRoot: null)
            .Should().BeTrue();
    }
}
