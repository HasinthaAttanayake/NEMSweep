using AwesomeAssertions;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Tests.Ingest;

namespace NEMSweep.CLI.Tests.QuickStart;

public sealed class QuickStartGuidanceTests
{
    // Fragments that are true only of the Advanced NEM sample. If any reaches the Simple guide, a reader
    // would take a dataset fact as a rule about the engine.
    private static readonly string[] NemFacts =
    [
        "typical meteorological year",
        "FY2025-26 NEM",
        "16,000 MWh of pumped hydro",
        "0.002% of demand unmet",
        "flat, full-load-factor demand",
    ];

    private static (string Output, string Guide) Prepare(string id)
    {
        string[] regions = id == "simple" ? ["Island-X"] : ["NSW1", "QLD1", "SA1", "TAS1", "VIC1"];
        using var bundle = new BundleFixture(regions, schemaVersion: 2);
        using var fixture = new StarterFixture();
        byte[] bytes = StarterFixture.PackDirectory(bundle.BundlePath);
        using var client = new HttpClient(new SampleHandler(bytes));
        using var output = new StringWriter();
        var router = new CommandRouter(fixture.Root, fixture.Root, output, TextWriter.Null,
            quickStartClient: client, samples: [StarterFixture.Sample(bytes, id)]);

        router.Run(["--quick-start", id]).Should().Be(0);

        string guide = File.ReadAllText(Path.Combine(fixture.Root, $"nemsweep-starter-{id}", "README.md"));
        return (output.ToString(), guide);
    }

    [Fact]
    public void ClosingMessage_SimpleSaysItIsSmallAndNeverMentionsTheFullSweep()
    {
        (string output, _) = Prepare("simple");

        output.Should().Contain("Prepared the Simple starter").And.Contain("runs in seconds")
            .And.Contain("sweeps/demo.json").And.Contain("scenarios/fixed-scenario.json")
            .And.Contain("scenarios/sized-scenario.json")
            .And.NotContain("datacentre-full").And.NotContain("Advanced full sweep");
    }

    [Fact]
    public void ClosingMessage_AdvancedNamesTheOptionalFullSweepAsExpensive()
    {
        (string output, _) = Prepare("advanced");

        output.Should().Contain("Prepared the Advanced starter").And.Contain("sweeps/datacentre-full.json")
            .And.Contain("about a minute per point, 25 points").And.Contain("sweeps/demo.json")
            .And.NotContain("runs in seconds");
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("advanced")]
    public void Guide_StatesTheEngineLimitationsForEveryStarter(string id)
    {
        (_, string guide) = Prepare(id);

        guide.Should().Contain("Limitations of the model")
            .And.Contain("no stochastic draws").And.Contain("merit order")
            .And.Contain("Only Battery capacity is sized").And.Contain("no foresight of later intervals")
            .And.Contain("not a multi-year expectation").And.Contain("not a retail price")
            .And.Contain("not a life-cycle figure")
            .And.Contain("https://docs.nemsweep.com/assumptions/limitations.html");
    }

    [Fact]
    public void Guide_AdvancedLabelsAndStatesTheNemSampleFacts()
    {
        (_, string guide) = Prepare("advanced");

        guide.Should().Contain("About this NEM sample data (not a property of the engine)");
        foreach (string fact in NemFacts)
        {
            guide.Should().Contain(fact);
        }
        guide.IndexOf("About this NEM sample data", StringComparison.Ordinal).Should()
            .BeGreaterThan(guide.IndexOf("Limitations of the model", StringComparison.Ordinal));
    }

    [Fact]
    public void Guide_SimpleNeverCarriesNemSampleFacts()
    {
        (_, string guide) = Prepare("simple");

        guide.Should().NotContain("About this NEM sample data");
        foreach (string fact in NemFacts)
        {
            guide.Should().NotContain(fact);
        }
    }
}
