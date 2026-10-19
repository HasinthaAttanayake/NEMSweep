using AwesomeAssertions;
using NEMSweep.CLI.Application;

namespace NEMSweep.CLI.Tests.Application;

public sealed class CliOptionsFlagTests
{
    [Fact]
    public void Parse_NeitherFlagGiven_LeavesBothUnset()
    {
        CliOptions options = CliOptions.Parse(["--run-sweep", "sweep.json"], out string[] remaining);

        options.Points.Should().BeNull();
        options.FailOnUnmet.Should().BeFalse();
        remaining.Should().Equal("--run-sweep", "sweep.json");
    }

    [Fact]
    public void Parse_PointsAndFailOnUnmet_AreStrippedFromTheCommandArguments()
    {
        CliOptions options = CliOptions.Parse(
            ["--run-sweep", "sweep.json", "--points", "p0,p9", "--fail-on-unmet"],
            out string[] remaining);

        options.Points.Should().Be("p0,p9");
        options.FailOnUnmet.Should().BeTrue();
        remaining.Should().Equal("--run-sweep", "sweep.json");
    }

    [Fact]
    public void Parse_PointsWithoutAValue_IsRejectedRatherThanTreatedAsAbsent()
    {
        Action parse = () => CliOptions.Parse(["--run-sweep", "sweep.json", "--points"], out _);

        parse.Should().Throw<UsageException>()
            .WithMessage("*--points requires a comma-separated list of point IDs*");
    }

    /// <summary>
    /// The flag can appear anywhere on the line, as the workspace overrides already can, so a
    /// caller does not have to remember an order the CLI reference does not document.
    /// </summary>
    [Fact]
    public void Parse_FlagsBeforeTheCommand_AreStillRecognised()
    {
        CliOptions options = CliOptions.Parse(
            ["--fail-on-unmet", "--points", "p1", "--run-sweep", "sweep.json"],
            out string[] remaining);

        options.Points.Should().Be("p1");
        options.FailOnUnmet.Should().BeTrue();
        remaining.Should().Equal("--run-sweep", "sweep.json");
    }
}
