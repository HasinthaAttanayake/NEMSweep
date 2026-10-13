using System.Net;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.QuickStart;
using NEMSweep.CLI.Tests.Ingest;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Tests.QuickStart;

public sealed class QuickStartCommandTests
{
    [Theory]
    [InlineData("--quick-start", "--csv")]
    [InlineData("--quick-start", "--format", "text")]
    [InlineData("--quick-start", "simple", "--data-root", "data")]
    [InlineData("--quick-start", "advanced", "--output", "out")]
    [InlineData("--output", "out", "--quick-start", "simple")]
    [InlineData("--quick-start", "invalid")]
    [InlineData("--quick-start", "simple", "study", "extra")]
    [InlineData("--quick-start", "simple", "")]
    public void Router_RejectsInvalidUsageBeforeHttp(params string[] args)
    {
        using var fixture = new StarterFixture();
        using var handler = new SampleHandler([1]);
        using var client = new HttpClient(handler);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var router = new CommandRouter(fixture.Root, fixture.Root, output, error,
            quickStartClient: client, samples: [StarterFixture.Sample([1])]);

        router.Run(args).Should().Be(2);

        error.ToString().Should().Contain("Usage:");
        handler.Calls.Should().Be(0);
        Directory.GetDirectories(fixture.Root).Should().BeEmpty();
    }

    [Fact]
    public void Router_RedirectedInputRequiresExplicitStarterWithoutReadingOrDownloading()
    {
        using var fixture = new StarterFixture();
        using var handler = new SampleHandler([1]);
        using var client = new HttpClient(handler);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var router = new CommandRouter(fixture.Root, fixture.Root, output, error,
            input: new StringReader("1"), inputRedirected: true,
            quickStartClient: client, samples: [StarterFixture.Sample([1])]);

        router.Run(["--quick-start"]).Should().Be(2);

        error.ToString().Should().Contain("Redirected input");
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("", "selection ended")]
    [InlineData("invalid\n", "selection ended")]
    public void Router_EofTerminatesInteractiveSelection(string input, string message)
    {
        using var fixture = new StarterFixture();
        using var client = new HttpClient(new SampleHandler([1]));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var router = new CommandRouter(fixture.Root, fixture.Root, output, error,
            new StringReader(input), false, client, [StarterFixture.Sample([1])]);

        router.Run(["--quick-start"]).Should().Be(2);

        error.ToString().Should().Contain(message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Router_RefusesExistingFileOrEmptyDirectoryBeforeHttp(bool directory)
    {
        using var fixture = new StarterFixture();
        string destination = Path.Combine(fixture.Root, "study");
        if (directory)
        {
            Directory.CreateDirectory(destination);
        }
        else
        {
            File.WriteAllText(destination, "keep");
        }
        using var handler = new SampleHandler([1]);
        using var client = new HttpClient(handler);
        using var error = new StringWriter();
        var router = new CommandRouter(fixture.Root, fixture.Root, TextWriter.Null, error,
            quickStartClient: client, samples: [StarterFixture.Sample([1])]);

        router.Run(["--quick-start", "simple", "study"]).Should().Be(1);

        error.ToString().Should().Contain("already exists");
        handler.Calls.Should().Be(0);
        if (!directory)
        {
            File.ReadAllText(destination).Should().Be("keep");
        }
    }

    [Theory]
    [InlineData("simple", false)]
    [InlineData("simple", true)]
    [InlineData("advanced", false)]
    [InlineData("advanced", true)]
    public void Router_PreparesExplicitAndInteractiveStudiesWithoutSettingsOrModelExecution(
        string id, bool interactive)
    {
        string[] regions = id == "simple" ? ["Island-X"] : ["NSW1", "QLD1", "SA1", "TAS1", "VIC1"];
        using var bundle = new BundleFixture(regions, schemaVersion: 2);
        using var fixture = new StarterFixture();
        byte[] bytes = StarterFixture.PackDirectory(bundle.BundlePath);
        using var client = new HttpClient(new SampleHandler(bytes));
        using var output = new StringWriter();
        using var error = new StringWriter();
        string input = id == "simple" ? "invalid\n1\n" : "2\n";
        var router = new CommandRouter(fixture.Root, fixture.Root, output, error,
            new StringReader(input), inputRedirected: !interactive,
            quickStartClient: client, samples: [StarterFixture.Sample(bytes, id)]);

        router.Run(interactive ? ["--quick-start"] : ["--quick-start", id]).Should().Be(0);

        string destination = Path.Combine(fixture.Root, $"nemsweep-starter-{id}");
        error.ToString().Should().BeEmpty();
        output.ToString().Should().Contain("Simple uses synthetic demand/weather")
            .And.Contain("real Australian NEM FY2026 AEMO demand")
            .And.Contain("original TMYx 2011-2025 EPW weather")
            .And.Contain("not measured FY2026 weather")
            .And.Contain($"Preparing {id} starter")
            .And.Contain("No simulations were run").And.Contain("--data-root ./data");
        Directory.GetFiles(Path.Combine(destination, "data")).Should().HaveCount(regions.Length * 2);
        Directory.Exists(Path.Combine(destination, "out")).Should().BeFalse();
        Directory.GetFiles(destination, "results*.json", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.GetDirectories(fixture.Root, ".nemsweep-staging-*").Should().BeEmpty();
        File.Exists(Path.Combine(destination, "download.zip")).Should().BeFalse();
        File.ReadAllText(Path.Combine(destination, "README.md")).Should().Contain(StarterFixture.Sample(bytes, id).Sha256);
        string guide = File.ReadAllText(Path.Combine(destination, "README.md"));
        if (id == "advanced")
        {
            guide.Should().Contain("1 July 2025-30 June 2026")
                .And.Contain("real AEMO operational demand")
                .And.Contain("original TMYx 2011-2025 EPW files")
                .And.Contain("not measured FY2026 weather")
                .And.NotContain("synthetic");
            JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "scenarios", "fixed-scenario.json")))!
                ["name"]!.GetValue<string>().Should().Contain("Australian NEM FY2026").And.NotContain("Synthetic");
            JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "sweeps", "datacentre-full.json")))!
                ["name"]!.GetValue<string>().Should().Contain("example assumptions").And.NotContain("Synthetic");
        }
        else
        {
            guide.Should().Contain("Simple demand and weather are synthetic");
        }

        string moved = Path.Combine(fixture.Root, "moved study with spaces");
        Directory.Move(destination, moved);
        WorkspacePaths paths = WorkspacePaths.FromRoots(moved, "data", "out");
        ScenarioSettings scenario = ScenarioConfig.Load(Path.Combine(moved, "scenarios", "fixed-scenario.json"));
        scenario.ExecutionMode.Should().Be(SimulationExecutionMode.FixedFleet);
        scenario.StorageSizing.Should().BeNull();
        scenario.SchemaVersion.Should().Be(ArtifactSchemaVersions.ScenarioConfig);
        SweepDefinition.Load("sweeps/demo.json", paths).Points.Should().HaveCount(2);
        if (id == "advanced")
        {
            SweepDefinition.Load("sweeps/datacentre-full.json", paths).Points.Should().HaveCount(25);
        }
    }

    [Fact]
    public void Router_FailedIngestionLeavesNoPartialStudyAndPreservesExistingFiles()
    {
        using var fixture = new StarterFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "keep.txt"), "keep");
        byte[] bytes = StarterFixture.Archive(("manifest.json", "{}", null));
        using var client = new HttpClient(new SampleHandler(bytes));
        using var error = new StringWriter();
        var router = new CommandRouter(fixture.Root, fixture.Root, TextWriter.Null, error,
            quickStartClient: client, samples: [StarterFixture.Sample(bytes)]);

        router.Run(["--quick-start", "simple", "study"]).Should().Be(1);

        error.ToString().Should().Contain("schemaVersion");
        Directory.GetDirectories(fixture.Root).Should().BeEmpty();
        File.ReadAllText(Path.Combine(fixture.Root, "keep.txt")).Should().Be("keep");
    }

    [Fact]
    public void Router_PrintsHostAppropriateNavigationWithQuotedDestination()
    {
        using var bundle = new BundleFixture(["Island-X"], schemaVersion: 2);
        using var fixture = new StarterFixture();
        byte[] bytes = StarterFixture.PackDirectory(bundle.BundlePath);
        using var client = new HttpClient(new SampleHandler(bytes));
        using var output = new StringWriter();
        string destination = Path.Combine(fixture.Root, "user's study");
        var router = new CommandRouter(fixture.Root, fixture.Root, output, TextWriter.Null,
            quickStartClient: client, samples: [StarterFixture.Sample(bytes)]);

        router.Run(["--quick-start", "simple", destination]).Should().Be(0);

        output.ToString().Should().Contain(OperatingSystem.IsWindows()
            ? $"Set-Location -LiteralPath '{destination.Replace("'", "''")}'"
            : $"cd '{destination.Replace("'", "'\"'\"'")}'");
    }

    [Fact]
    public void Router_FailedDownloadLeavesNoPartialStudy()
    {
        using var fixture = new StarterFixture();
        using var client = new HttpClient(new SampleHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        using var error = new StringWriter();
        var router = new CommandRouter(fixture.Root, fixture.Root, TextWriter.Null, error,
            quickStartClient: client, samples: [StarterFixture.Sample([1])]);

        router.Run(["--quick-start", "simple", "study"]).Should().Be(1);

        error.ToString().Should().StartWith("Quick-start failed:").And.Contain("HTTP 404");
        Directory.GetDirectories(fixture.Root).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Router_InvalidHashOrUnsafeArchiveLeavesNoPartialStudy(bool badHash)
    {
        using var fixture = new StarterFixture();
        byte[] bytes = StarterFixture.Archive(("../escape.txt", "unsafe", null));
        StarterSample sample = StarterFixture.Sample(bytes);
        if (badHash)
        {
            sample = sample with { Sha256 = new string('0', 64) };
        }
        using var client = new HttpClient(new SampleHandler(bytes));
        using var error = new StringWriter();
        var router = new CommandRouter(fixture.Root, fixture.Root, TextWriter.Null, error,
            quickStartClient: client, samples: [sample]);

        router.Run(["--quick-start", "simple", "study"]).Should().Be(1);

        error.ToString().Should().Contain(badHash ? "SHA-256" : "Unsafe");
        Directory.GetFileSystemEntries(fixture.Root).Should().BeEmpty();
    }

    [Fact]
    public void Router_TimeoutIsAnOperationalFailureAndCleansStaging()
    {
        using var fixture = new StarterFixture();
        using var client = new HttpClient(new SampleHandler((_, _) =>
            throw new TaskCanceledException("Download timed out.")));
        using var error = new StringWriter();
        var router = new CommandRouter(fixture.Root, fixture.Root, TextWriter.Null, error,
            quickStartClient: client, samples: [StarterFixture.Sample([1])]);

        router.Run(["--quick-start", "simple", "study"]).Should().Be(1);

        error.ToString().Should().StartWith("Quick-start failed:").And.Contain("timed out");
        Directory.GetFileSystemEntries(fixture.Root).Should().BeEmpty();
    }

    [Fact]
    public async Task Prepare_CancelledDownloadCleansStaging()
    {
        using var fixture = new StarterFixture();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new SampleHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        Func<Task> act = () => QuickStartCommand.PrepareAsync(StarterFixture.Sample([1]),
            Path.Combine(fixture.Root, "study"), TextWriter.Null, TextWriter.Null, client, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        Directory.GetDirectories(fixture.Root).Should().BeEmpty();
    }

    [Fact]
    public async Task Prepare_ConcurrentDestinationCreationNeverOverwritesIt()
    {
        using var bundle = new BundleFixture(["Island-X"], schemaVersion: 2);
        using var fixture = new StarterFixture();
        string destination = Path.Combine(fixture.Root, "study");
        byte[] bytes = StarterFixture.PackDirectory(bundle.BundlePath);
        using var client = new HttpClient(new SampleHandler((_, _) =>
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }));
        Func<Task> act = () => QuickStartCommand.PrepareAsync(StarterFixture.Sample(bytes),
            destination, TextWriter.Null, TextWriter.Null, client);

        await act.Should().ThrowAsync<IOException>().WithMessage("*already exists*");

        Directory.GetDirectories(fixture.Root).Should().Equal(destination);
        Directory.GetFiles(destination).Should().Equal(Path.Combine(destination, "keep.txt"));
        File.ReadAllText(Path.Combine(destination, "keep.txt")).Should().Be("keep");
    }

    [Fact]
    public void Templates_AdvancedPreservesAllFleetCostProfileLinkAndSweepAssumptions()
    {
        using var fixture = new StarterFixture();
        var context = new CliContext(WorkspacePaths.FromRoots(fixture.Root, "data", "out"),
            new CliSettings("bundle", "data", "out", "unused"), TextWriter.Null);
        using Stream scenarioSource = typeof(CommandRouter).Assembly.GetManifestResourceStream("Starter.Advanced")!;
        JsonObject original = ScenarioConfig.UpgradeConfig(JsonNode.Parse(scenarioSource)!.AsObject());
        using Stream sweepSource = typeof(CommandRouter).Assembly.GetManifestResourceStream("Starter.FullSweep")!;
        JsonObject originalSweep = JsonNode.Parse(sweepSource)!.AsObject();

        StarterTemplates.WriteAndValidate("advanced", context);

        JsonObject sized = JsonNode.Parse(File.ReadAllBytes(
            Path.Combine(fixture.Root, "scenarios", "sized-scenario.json")))!.AsObject();
        foreach (string property in new[] { "regions", "interconnectors", "costBasis", "storageSizing", "reliability" })
        {
            JsonNode.DeepEquals(original[property], sized[property]).Should().BeTrue(property);
        }
        JsonObject sweep = JsonNode.Parse(File.ReadAllBytes(
            Path.Combine(fixture.Root, "sweeps", "datacentre-full.json")))!.AsObject();
        JsonNode.DeepEquals(originalSweep["points"], sweep["points"]).Should().BeTrue();
        JsonNode.DeepEquals(originalSweep["axis"], sweep["axis"]).Should().BeTrue();
        ScenarioSettings fixedScenario = ScenarioConfig.Load(
            Path.Combine(fixture.Root, "scenarios", "fixed-scenario.json"));
        fixedScenario.StorageSizing.Should().BeNull();
        fixedScenario.ExecutionMode.Should().Be(SimulationExecutionMode.FixedFleet);
    }
}
