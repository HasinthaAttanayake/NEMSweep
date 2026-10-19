using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.Ingest;
using NEMSweep.CLI.QuickStart;

namespace NEMSweep.CLI.Application;

internal static class QuickStartCommand
{
    public static int Run(
        string[] args, string workingRoot, TextReader input, bool inputRedirected,
        TextWriter output, TextWriter error, HttpClient client, IReadOnlyList<StarterSample> samples)
    {
        if (args.Length > 3)
        {
            throw new UsageException("Use --quick-start [simple|advanced] [new-study-directory].");
        }
        output.WriteLine("Simple: one Island-X region with readable fleet and battery-sizing examples.");
        output.WriteLine("Advanced: five connected NEM regions with the existing fleet/cost/link assumptions.");
        output.WriteLine("Simple uses synthetic demand/weather.");
        output.WriteLine("Advanced uses real Australian NEM FY2026 AEMO demand and original TMYx 2011-2025 EPW weather.");
        output.WriteLine("Typical-year weather is not measured FY2026 weather; scenario assumptions are examples, not certified results.");
        output.WriteLine("Preparation downloads and ingests inputs; it does not run simulations or sweeps.");

        string id;
        if (args.Length == 1)
        {
            if (inputRedirected)
            {
                throw new UsageException("Redirected input requires --quick-start simple or --quick-start advanced.");
            }
            while (true)
            {
                output.Write("Choose 1 (simple) or 2 (advanced): ");
                string? answer = input.ReadLine();
                if (answer is null)
                {
                    throw new UsageException("Quick-start selection ended without a choice.");
                }
                string choice = answer.Trim().ToLowerInvariant();
                if (choice is "1" or "simple" or "2" or "advanced")
                {
                    id = choice is "1" or "simple" ? "simple" : "advanced";
                    break;
                }
                output.WriteLine("Enter 1/simple or 2/advanced.");
            }
        }
        else
        {
            id = args[1];
            if (id is not ("simple" or "advanced"))
            {
                throw new UsageException("Quick-start sample must be 'simple' or 'advanced'.");
            }
        }

        string destination = WorkspacePaths.FromRoots(workingRoot, "data", "out")
            .ResolveConfiguredPath(args.Length == 3 ? args[2] : $"nemsweep-starter-{id}");
        if (string.IsNullOrWhiteSpace(args.Last()) || Path.GetPathRoot(destination) == destination)
        {
            throw new UsageException("Quick-start requires a new study directory, not a filesystem root.");
        }
        RefuseExistingDestination(destination);
        StarterSample sample = samples.SingleOrDefault(sample => sample.Id == id)
            ?? throw new FormatException($"Starter '{id}' is missing from this CLI build's catalogue.");
        output.WriteLine($"Preparing {id} starter, data revision {sample.Revision}.");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            PrepareAsync(sample, destination, output, error, client, cancellation.Token).GetAwaiter().GetResult();
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
        output.WriteLine($"Prepared study: {destination}");
        WriteRunInstructions(output, destination, sample);
        return 0;
    }

    internal static async Task PrepareAsync(
        StarterSample sample, string destination, TextWriter output, TextWriter error,
        HttpClient client, CancellationToken cancellationToken = default)
    {
        RefuseExistingDestination(destination);
        string parent = Path.GetDirectoryName(destination)
            ?? throw new IOException("The study destination must have a parent directory.");
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, $".nemsweep-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            string archivePath = Path.Combine(stage, "download.zip");
            await StarterArchive.DownloadAsync(client, sample, archivePath, timeout.Token);
            string bundle = Path.Combine(stage, "bundle");
            StarterArchive.Extract(archivePath, bundle, timeout.Token);
            File.Delete(archivePath);

            var settings = new CliSettings("bundle", "data", "out", "scenarios/fixed-scenario.json");
            WorkspacePaths paths = WorkspacePaths.FromRoots(stage, "data", "out");
            using var ingestionOutput = new StringWriter();
            var context = new CliContext(paths, settings, ingestionOutput, error);
            IngestCommand.Run(context, bundle);
            StarterTemplates.WriteAndValidate(sample.Id, context);
            File.WriteAllText(Path.Combine(stage, "README.md"), StudyGuide(sample));
            timeout.Token.ThrowIfCancellationRequested();
            RefuseExistingDestination(destination);
            Directory.Move(stage, destination);
            output.WriteLine("Download verified; bundle ingested and all scenarios/sweep points validated.");
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                try
                {
                    Directory.Delete(stage, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    error.WriteLine($"Could not clean owned quick-start staging directory '{stage}': {exception.Message}");
                    throw;
                }
            }
        }
    }

    private static void RefuseExistingDestination(string path)
    {
        if (Path.Exists(path))
        {
            throw new IOException($"Study destination already exists; nothing was changed: {path}");
        }
    }

    private static string StudyGuide(StarterSample sample) => $"""
        # NEMSweep {sample.Id} starter

        {(sample.Id == "simple"
            ? "Simple demand and weather are synthetic, not measured data."
            : "Advanced represents Australia's NEM FY2026 (1 July 2025-30 June 2026).\nIt preserves real AEMO operational demand and original TMYx 2011-2025 EPW files.\nThe EPW typical-year profiles are not measured FY2026 weather.")}
        Scenario assumptions belong to these examples, not forecasts or certified
        results; review and edit them before using this tool for your own study.

        Data revision: {sample.Revision}
        Source: {sample.Url}
        Archive SHA-256: {sample.Sha256}

        The raw format-2 input bundle is in `bundle/`; ingested demand/weather
        are in `data/`. Configurations use the CLI's current scenario/sweep specs.
        Fixed-fleet execution uses exactly the declared fleet; sizeBattery may
        add battery storage within the bounds. It does not size pumped hydro.
        The study uses one fixed UTC+10 market offset, with no daylight saving.

        From this study directory:

        ```powershell
        nemsweep --run-scenario scenarios/fixed-scenario.json --data-root ./data --output ./out/fixed
        nemsweep --run-scenario scenarios/sized-scenario.json --data-root ./data --output ./out/sized
        nemsweep --run-sweep sweeps/demo.json --data-root ./data --output ./out/demo --csv
        ```

        Preparation has not run any model. The two-point mode sweep compares a
        fixed baseline with sizing; Simple's standalone fixed scenario instead
        has 80 MW of Gas to make a readable first run, so the sweep's `fixed`
        point is not that scenario.
        {(sample.Id == "simple" ? SimpleSweeps : AdvancedSweeps)}
        All baseline paths are relative to this study directory, so it can be moved.

        {EngineLimitations}
        {(sample.Id == "simple" ? "" : "\n" + NemSampleDataFacts)}
        Input formats: https://github.com/HasinthaAttanayake/NEMSweep/blob/main/docs/guide/schemas.md
        Samples: https://github.com/HasinthaAttanayake/NEMSweep.Samples
        """;

    /// <summary>
    /// What else each starter carries, named per starter so a guide never points at a file its own
    /// study does not contain.
    /// </summary>
    private const string SimpleSweeps = "This starter supplies no further sweeps.";

    private const string AdvancedSweeps = """
        This starter also supplies `sweeps/datacentre-full.json` (25 points,
        potentially substantial compute); its fleet/cost/link assumptions and sweep
        overrides match the repository example, alongside its original AEMO demand
        and EPWs.
        """;

    /// <summary>
    /// Limitations of the engine itself, true of every run whatever the data. They are kept apart from
    /// <see cref="NemSampleDataFacts"/> so a reader never mistakes a property of the NEM sample for a rule
    /// about the model and then misdescribes their own study. Nothing dataset-specific belongs here.
    /// </summary>
    private const string EngineLimitations = """

        ## Limitations of the model

        These hold for every NEMSweep run, whatever data it uses.

        - This is one system against the demand and weather supplied, with no stochastic draws. It reports
          a realised outcome, not an expectation; the difference between two scenarios is more trustworthy
          than the level of either.
        - Generation is dispatched in merit order by short-run marginal cost. There is no market bidding,
          no unit commitment, no security-constrained dispatch and no investment valuation.
        - Only Battery capacity is sized. Every other storage technology, all generation and all
          transmission stay exactly as the scenario declared them.
        - Storage charge and discharge follows a fixed rule with no foresight of later intervals, so the
          capacity the search settles on can be too small against a prolonged shortfall or too large
          against a system able to arbitrage.
        - The reliability target is the one the scenario set, and it is tested over the modelled period
          only. It is not a multi-year expectation.
        - Cost is the levelised cost of building and running the declared system per MWh served. It
          excludes network, retail, scheme and tax costs, so it is not a retail price.
        - Emissions are operational combustion only, charged on generated energy and reported per MWh
          served. They are not a life-cycle figure.

        Details: <https://docs.nemsweep.com/assumptions/limitations.html>
        """;

    /// <summary>
    /// Facts about the Advanced starter's NEM sample data only. They are labelled as dataset facts and are
    /// written only into the Advanced guide, because the synthetic Simple starter shares none of them.
    /// </summary>
    private const string NemSampleDataFacts = """
        ## About this NEM sample data (not a property of the engine)

        These facts describe the Advanced starter's data and scenarios, and nothing else.

        - Weather is one typical meteorological year per region, assembled from 2011-2025 observations.
          Hard years such as a long wind drought are absent, so storage need is probably understated.
        - The fleet is the FY2025-26 NEM, with about 16,000 MWh of pumped hydro held fixed and excluded
          from the battery figures.
        - The reliability target is 0.002% of demand unmet, the level of the NEM reliability standard.
        - The data-centre sweep adds flat, full-load-factor demand across the regions. It adds no new
          generation and no new transmission.
        - Scenario assumptions are examples, not forecasts, calibrated models or certified results.
        """;

    private static void WriteRunInstructions(TextWriter output, string destination, StarterSample sample)
    {
        bool advanced = sample.Id != "simple";
        output.WriteLine(advanced
            ? "Prepared the Advanced starter: five connected NEM regions."
            : "Prepared the Simple starter: the smallest starter, and it runs in seconds.");
        output.WriteLine("No simulations were run. Change to the study directory before running:");
        output.WriteLine(OperatingSystem.IsWindows()
            ? $"  Set-Location -LiteralPath '{destination.Replace("'", "''")}'  # PowerShell"
            : $"  cd '{destination.Replace("'", "'\"'\"'")}'  # POSIX shell");
        output.WriteLine("  nemsweep --run-scenario scenarios/fixed-scenario.json --data-root ./data --output ./out/fixed");
        output.WriteLine("  nemsweep --run-scenario scenarios/sized-scenario.json --data-root ./data --output ./out/sized");
        output.WriteLine("  nemsweep --run-sweep sweeps/demo.json --data-root ./data --output ./out/demo --csv");
        if (advanced)
        {
            output.WriteLine("Optional and expensive, not run by quick-start (about a minute per point, 25 points):");
            output.WriteLine("  nemsweep --run-sweep sweeps/datacentre-full.json --data-root ./data --output ./out/datacentre-full --csv");
        }
        output.WriteLine("See the study README for assumptions and limitations.");
    }
}
