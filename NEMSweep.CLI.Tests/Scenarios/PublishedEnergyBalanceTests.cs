using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.Scenarios;
using NEMSweep.Contracts;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;
using Xunit.Abstractions;

namespace NEMSweep.CLI.Tests.Scenarios;

public sealed class PublishedEnergyBalanceTests(ITestOutputHelper output)
{
    internal const string SweepDirectoryVariable = "NEMSWEEP_ENERGY_BALANCE_SWEEP";
    internal const string DataDirectoryVariable = "NEMSWEEP_ENERGY_BALANCE_DATA";
    private const double PublishedQuantum = 0.1;
    private static readonly IReadOnlyDictionary<string, (string Demand, string Weather)> ExpectedInputDigests =
        new Dictionary<string, (string Demand, string Weather)>
        {
            ["NSW1"] = ("3baec9ac6376efde99e06cd6aef3a7429aad0bc15b644b2a8907f712202e231a", "47fe6a91d3a5228886a81ba72ef35ed56c216a7cb1fb06b4eb24ed2478d34aea"),
            ["QLD1"] = ("6ecd5550a8d262493decd28e60cdeb4010051444da41a130970ed755e794e915", "bf3631fcf2a51ef494bf5e1efa937120c688d882133b0683581609dcd570ebb4"),
            ["SA1"] = ("61e5c304f9a3d6ed84ee12fdbf84a9b2d23e10e333031a31c9ae4b3ab5c782b1", "95d3033934d76bea1c61ac0e807043464b34707ba269a7f04caa6f28b7c0320a"),
            ["TAS1"] = ("7a88d362da47ce07c11fc930d8467162e6e7bb5cf6919c1a5a71bb133cf6725c", "46819d2ad1630200fcf52fa224cb713616dc260a9d62b67b00e427b30ca69971"),
            ["VIC1"] = ("cac7c366eab7bfbe1d680c752c02cf54e32ba727dbd377a95dfc2c749d218048", "5f36d2143b6842ea25869ca82600ac869439385d902ae3ed6700508cba9cd161"),
        };

    [Fact]
    public void PublicationPrecision_MatchesTheBalanceRoundingBound()
    {
        string[] fields =
        [
            "deliveredGenerationByTechnologyMw", "totalDemandMw", "unservedDemandMw",
            "chargeMw", "dischargeMw", "importsMw", "exportsMw", "transmissionLossesMw",
            "curtailmentMw", "deliveredGenerationMwh", "energyServedMwh",
        ];
        foreach (string field in fields)
        {
            Math.Pow(10, -JsonFile.DecimalPlaces(field)).Should().Be(PublishedQuantum, field);
        }
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Balance_IncludesStorageDischargeAndSendingEndExports(double intervalHours)
    {
        // Hour 1 charges 20 MW from 140 MW generation; hour 2 discharges 16 MW.
        var series = new DispatchSeriesDTO(
            new DispatchDemandDTO([90, 126], [], [90, 126]),
            new Dictionary<string, double[]> { ["Gas"] = [100, 100] },
            [20, 0], [0, 2], [20, 0], [0, 16], [],
            [0, 8], [10, 0], [0, 2]);

        Terms terms = CheckSeries("fixture", series, TimeSpan.FromHours(intervalHours));

        terms.Generation.Should().BeApproximately(240 * intervalHours, 1e-9);
        terms.Served.Should().BeApproximately(214 * intervalHours, 1e-9);
        terms.Delivered.Should().BeApproximately(200 * intervalHours, 1e-9);
        terms.Charge.Should().BeApproximately(20 * intervalHours, 1e-9);
        terms.Discharge.Should().BeApproximately(16 * intervalHours, 1e-9);
    }

    [Fact]
    public void Balance_RejectsAMissingDischargeTerm()
    {
        var series = new DispatchSeriesDTO(
            new DispatchDemandDTO([116], [], [116]),
            new Dictionary<string, double[]> { ["Gas"] = [100] },
            [0], [0], [0], [0], [], [0], [0], [0]);

        Action check = () => CheckSeries("missing discharge", series, TimeSpan.FromHours(1));

        check.Should().Throw<Xunit.Sdk.XunitException>()
            .WithMessage("*missing discharge*");
    }

    [PublishedSweepTheory]
    [Trait("Category", "PublishedEnergyBalance")]
    [InlineData("p0", 186643742.000000000, 186684136.434674323, 24973.471683893, 65367.906358062)]
    [InlineData("p9", 226060637.654202789, 225408513.652390033, 1073308.604923365, 421184.603110420)]
    [InlineData("p10", 230388177.874134779, 229475558.050264090, 1386341.820888956, 473721.997017818)]
    [InlineData("p24", 278114778.487484276, 271480898.970761597, 7522816.331588686, 888936.814864491)]
    public void PublishedPoint_ClosesHourlyAndIntegratedRegionalAndSystemBalances(
        string pointId, double expectedServed, double expectedDelivered, double expectedDischarge, double expectedLoss)
    {
        string sweepDirectory = Environment.GetEnvironmentVariable(SweepDirectoryVariable)
            ?? throw new InvalidOperationException($"Set {SweepDirectoryVariable} to a generated sweep directory.");
        SweepIndexDTO index = Read<SweepIndexDTO>(Path.Combine(sweepDirectory, "index.json"));
        index.SweepId.Should().Be("datacentre-nameplate-fy2026");
        index.Provenance.InputFiles.Single(input => input.Purpose == "baseline-scenario-config").Sha256
            .Should().Be("ba33825c5af80dc74bcb7d076e186839cfbc67c97e45b02d8b028871d9cfa3bc",
                "the reproduction uses the original baseline configuration at 7f44412");
        SweepIndexPointDTO point = index.Points.Single(entry => entry.PointId == pointId);
        point.Status.Should().Be(SweepPointStatus.Succeeded);
        SystemDispatchResultsDTO system = Read<SystemDispatchResultsDTO>(
            Path.Combine(sweepDirectory, point.DetailPath!));
        system.ExecutionMode.Should().Be(SimulationExecutionMode.SizeBattery);
        system.Resolution.Should().Be(TimeSpan.FromHours(1));
        system.DataSeries.Demand.TotalDemandMw.Should().HaveCount(8760);
        system.DataSourcesByRegion.Keys.Should().BeEquivalentTo(ExpectedInputDigests.Keys);
        foreach ((string regionId, (string demand, string weather)) in ExpectedInputDigests)
        {
            system.DataSourcesByRegion[regionId].DemandInput.Sha256.Should().Be(demand, $"{regionId} Advanced demand bytes");
            system.DataSourcesByRegion[regionId].WeatherInput.Sha256.Should().Be(weather, $"{regionId} Advanced weather bytes");
        }

        Terms systemTerms = CheckSeries($"{pointId}/system/published", system.DataSeries, system.Resolution,
            PublishedQuantum);
        CheckMetrics(systemTerms, system.Metrics, point.Scalars!, isSystem: true);
        AssertClose(systemTerms.Exports - systemTerms.Imports, systemTerms.TransmissionLoss,
            "system sending-end exports minus received imports equals transmission loss",
            3 * systemTerms.SampleError);
        AssertClose(systemTerms.TransmissionLoss,
            system.Interconnectors.Sum(link => link.LossesMw.Sum()) * system.Resolution.TotalHours,
            "system transmission loss equals the sum of directed-link losses",
            (1 + system.Interconnectors.Length) * systemTerms.SampleError);

        var regions = new List<Terms>();
        foreach (SweepPointRegionDetailDTO detail in point.RegionDetails!)
        {
            RegionDispatchResultsDTO region = Read<RegionDispatchResultsDTO>(
                Path.Combine(sweepDirectory, detail.DetailPath));
            region.RunId.Should().Be(system.RunId);
            region.Resolution.Should().Be(system.Resolution);
            region.PeriodStart.Should().Be(system.PeriodStart);
            region.PeriodEnd.Should().Be(system.PeriodEnd);
            region.RegionId.Should().Be(detail.RegionId);
            Terms terms = CheckSeries($"{pointId}/{region.RegionId}/published", region.DataSeries, region.Resolution,
                PublishedQuantum);
            CheckMetrics(terms, region.Metrics,
                point.RegionScalars!.Single(entry => entry.RegionId == region.RegionId).Scalars);
            regions.Add(terms);
            foreach ((string technology, double[] values) in region.DataSeries.DeliveredGenerationByTechnologyMw)
            {
                AssertClose(system.RegionSummariesById[region.RegionId].DeliveredGenerationByTechnologyMwh[technology],
                    values.Sum() * region.Resolution.TotalHours,
                    $"{pointId}/{region.RegionId}/{technology}: published technology total",
                    terms.SampleError + 0.05);
            }
        }

        point.RegionDetails!.Select(detail => detail.RegionId).Should().BeEquivalentTo(system.RegionIds);
        regions.Should().HaveCount(system.RegionIds.Length);
        double aggregateError = systemTerms.SampleError + regions.Sum(region => region.SampleError);
        AssertClose(systemTerms.Delivered, regions.Sum(region => region.Delivered), "regional delivered sum",
            systemTerms.DeliveredError + regions.Sum(region => region.DeliveredError));
        AssertClose(systemTerms.Demand, regions.Sum(region => region.Demand), "regional demand sum", aggregateError);
        AssertClose(systemTerms.Unserved, regions.Sum(region => region.Unserved), "regional unserved sum", aggregateError);
        AssertClose(systemTerms.Charge, regions.Sum(region => region.Charge), "regional charge sum", aggregateError);
        AssertClose(systemTerms.Discharge, regions.Sum(region => region.Discharge), "regional discharge sum", aggregateError);
        AssertClose(systemTerms.Curtailment, regions.Sum(region => region.Curtailment), "regional curtailment sum", aggregateError);
        AssertClose(systemTerms.Imports, regions.Sum(region => region.Imports), "regional imports sum", aggregateError);
        AssertClose(systemTerms.Exports, regions.Sum(region => region.Exports), "regional exports sum", aggregateError);
        AssertClose(systemTerms.TransmissionLoss, regions.Sum(region => region.TransmissionLoss),
            "regional incoming transmission losses sum", aggregateError);

        string dataDirectory = Environment.GetEnvironmentVariable(DataDirectoryVariable)
            ?? throw new InvalidOperationException($"Set {DataDirectoryVariable} to the Advanced starter's data directory.");
        ScenarioDispatchResult dispatch = ScenarioRunner.RunDispatch(
            ScenarioConfig.Load(Path.Combine(sweepDirectory, point.ConfigPath!)),
            WorkspacePaths.FromRoots(sweepDirectory, dataDirectory, sweepDirectory));
        foreach ((string regionId, (string demand, string weather)) in ExpectedInputDigests)
        {
            dispatch.DemandInputs[regionId].Artifact.Sha256.Should().Be(demand, $"{regionId} rerun demand bytes");
            dispatch.WeatherInputs[regionId].Artifact.Sha256.Should().Be(weather, $"{regionId} rerun weather bytes");
        }
        foreach (DispatchOutcome outcome in dispatch.Dispatch.RegionalOutcomes)
        {
            Terms engineTerms = CheckEngine($"{pointId}/{outcome.RegionId}/engine", outcome);
            CheckMetrics(engineTerms, system.RegionSummariesById[outcome.RegionId].Metrics,
                point.RegionScalars!.Single(entry => entry.RegionId == outcome.RegionId).Scalars);
            foreach ((GenerationTechnology technology, FlowSeries flow) in outcome.PerFleetDelivered)
            {
                AssertClose(system.RegionSummariesById[outcome.RegionId].DeliveredGenerationByTechnologyMwh[technology.ToString()],
                    flow.Integrate().MegawattHours, $"{pointId}/{outcome.RegionId}/{technology}: unrounded technology total", 0.05);
            }
        }

        SystemDispatchOutcome engine = SystemDispatchOutcome.Create(dispatch.PowerSystem, dispatch.Dispatch);
        double served = engine.EnergyServed.Integrate().MegawattHours;
        double delivered = engine.PerFleetDelivered.Values.Sum(flow => flow.Integrate().MegawattHours);
        double discharge = engine.Discharge.Integrate().MegawattHours;
        double loss = engine.TransmissionLosses.Integrate().MegawattHours;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{pointId}/system/engine: MWh served={served:F9}, delivered={delivered:F9}, discharge={discharge:F9}, transmissionLoss={loss:F9}, residual={delivered + discharge - loss - served:E9}"));
        served.Should().BeApproximately(expectedServed, 1e-4, $"{pointId} documented v0.2.0 energy served");
        delivered.Should().BeApproximately(expectedDelivered, 1e-4, $"{pointId} documented v0.2.0 delivered generation");
        discharge.Should().BeApproximately(expectedDischarge, 1e-4, $"{pointId} documented v0.2.0 discharge");
        loss.Should().BeApproximately(expectedLoss, 1e-4, $"{pointId} documented v0.2.0 transmission loss");
        AssertClose(served, delivered + discharge - loss, "unrounded system balance");
        AssertClose(point.Scalars!.EnergyServedMwh, served, "unrounded system served vs published scalar", 0.05);
        AssertClose(point.Scalars.DeliveredGenerationMwh, delivered, "unrounded system delivered vs published scalar", 0.05);
    }

    private Terms CheckEngine(string scope, DispatchOutcome outcome)
    {
        var series = new DispatchSeriesDTO(
            new DispatchDemandDTO(Values(outcome.NativeDemand), [], Values(outcome.Demand)),
            outcome.PerFleetDelivered.ToDictionary(entry => entry.Key.ToString(), entry => Values(entry.Value)),
            Values(outcome.Curtailment), Values(outcome.Unserved), Values(outcome.Charge),
            Values(outcome.Discharge), [], Values(outcome.Imports), Values(outcome.Exports),
            new double[outcome.Demand.Length]);
        Terms terms = CheckSeries(scope, series, outcome.Demand.Resolution);
        AssertClose(terms.Generation,
            outcome.PerFleetGeneration.Values.Sum(flow => flow.Integrate().MegawattHours),
            $"{scope}: independently recorded generation vs delivered + charge + curtailment");
        for (int i = 0; i < outcome.Demand.Length; i++)
        {
            double generation = outcome.PerFleetGeneration.Values.Sum(flow => flow[i].Megawatts);
            double inputs = generation + outcome.Discharge[i].Megawatts + outcome.Imports[i].Megawatts
                + outcome.Unserved[i].Megawatts;
            double outputs = outcome.Demand[i].Megawatts + outcome.Charge[i].Megawatts
                + outcome.Exports[i].Megawatts + outcome.Curtailment[i].Megawatts;
            AssertClose(inputs, outputs, $"{scope}: unrounded generation balance at interval {i}");
        }
        return terms;
    }

    private static double[] Values(FlowSeries flow) =>
        Enumerable.Range(0, flow.Length).Select(i => flow[i].Megawatts).ToArray();

    private Terms CheckSeries(string scope, DispatchSeriesDTO series, TimeSpan resolution, double quantum = 0)
    {
        int length = series.Demand.TotalDemandMw.Length;
        double hours = resolution.TotalHours;
        double[][] flows =
        [
            series.UnservedDemandMw, series.CurtailmentMw, series.ChargeMw, series.DischargeMw,
            series.ImportsMw, series.ExportsMw, series.TransmissionLossesMw,
            .. series.DeliveredGenerationByTechnologyMw.Values,
        ];
        foreach (double[] flow in flows)
        {
            flow.Should().HaveCount(length, scope);
            flow.Should().OnlyContain(value => double.IsFinite(value) && value >= -1e-9, scope);
        }

        var terms = new Terms(
            series.Demand.TotalDemandMw.Sum() * hours,
            series.UnservedDemandMw.Sum() * hours,
            series.DeliveredGenerationByTechnologyMw.Values.Sum(values => values.Sum()) * hours,
            series.CurtailmentMw.Sum() * hours,
            series.ChargeMw.Sum() * hours,
            series.DischargeMw.Sum() * hours,
            series.ImportsMw.Sum() * hours,
            series.ExportsMw.Sum() * hours,
            series.TransmissionLossesMw.Sum() * hours,
            quantum / 2 * length * hours, series.DeliveredGenerationByTechnologyMw.Count);
        double roundingError = terms.DeliveredError + 5 * terms.SampleError;
        string report = string.Create(CultureInfo.InvariantCulture,
            $"{scope}: MWh demand={terms.Demand:F9}, unserved={terms.Unserved:F9}, " +
            $"served={terms.Served:F9}, delivered={terms.Delivered:F9}, " +
            $"generation(inferred)={terms.Generation:F9}, curtailment={terms.Curtailment:F9}, " +
            $"charge={terms.Charge:F9}, discharge={terms.Discharge:F9}, " +
            $"imports(received)={terms.Imports:F9}, exports(sent)={terms.Exports:F9}, " +
            $"transmissionLoss={terms.TransmissionLoss:F9}, served-delivered={terms.Served - terms.Delivered:F9}, " +
            $"residual={terms.Delivered + terms.Discharge + terms.Imports - terms.Exports - terms.Served:E9}, " +
            $"roundingBound={roundingError:F9}, " +
            $"numericTolerance={Tolerance(terms.Served, terms.Delivered + terms.Discharge + terms.Imports):E9}");
        output.WriteLine(report);
        AssertClose(terms.Delivered + terms.Discharge + terms.Imports, terms.Served + terms.Exports, report,
            roundingError);
        AssertClose(terms.Generation + terms.Discharge + terms.Imports + terms.Unserved,
            terms.Demand + terms.Charge + terms.Exports + terms.Curtailment, report, roundingError);

        for (int i = 0; i < length; i++)
        {
            double delivered = series.DeliveredGenerationByTechnologyMw.Values.Sum(values => values[i]);
            AssertClose((delivered + series.DischargeMw[i] + series.ImportsMw[i]) * hours,
                (series.Demand.TotalDemandMw[i] - series.UnservedDemandMw[i] + series.ExportsMw[i]) * hours,
                $"{report}; interval={i}", quantum / 2 * (terms.Technologies + 5) * hours);
        }

        return terms;
    }

    private static void CheckMetrics(Terms terms, DispatchMetricsDTO metrics, SweepPointScalarResultsDTO scalars,
        bool isSystem = false)
    {
        AssertClose(metrics.DemandMwh, terms.Demand, "metrics demand", terms.SampleError + 0.05);
        AssertClose(metrics.DeliveredGenerationMwh, terms.Delivered, "metrics delivered generation", terms.DeliveredError + 0.05);
        AssertClose(metrics.UnservedEnergyMwh, terms.Unserved, "metrics unserved", terms.SampleError + 0.05);
        AssertClose(metrics.CurtailedEnergyMwh, terms.Curtailment, "metrics curtailment", terms.SampleError + 0.05);
        AssertClose(scalars.DemandMwh, terms.Demand, "scalars demand", terms.SampleError + 0.05);
        AssertClose(scalars.EnergyServedMwh, terms.Served, "scalars energy served", 2 * terms.SampleError + 0.05);
        AssertClose(scalars.DeliveredGenerationMwh, terms.Delivered, "scalars delivered generation", terms.DeliveredError + 0.05);
        AssertClose(scalars.UnservedEnergyMwh, terms.Unserved, "scalars unserved", terms.SampleError + 0.05);
        AssertClose(scalars.CurtailedEnergyMwh, terms.Curtailment, "scalars curtailment", terms.SampleError + 0.05);
        AssertClose(scalars.NetImportedEnergyMwh, isSystem ? 0 : terms.Imports - terms.Exports,
            "scalars net imports (system excludes internal transfers)", isSystem ? 0 : 2 * terms.SampleError + 0.05);
    }

    private static double Tolerance(double left, double right) =>
        Math.Max(1e-6, 1e-9 * Math.Max(Math.Abs(left), Math.Abs(right)));

    private static void AssertClose(double left, double right, string reason, double roundingError = 0) =>
        left.Should().BeApproximately(right, roundingError + Tolerance(left, right), reason);

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), JsonFile.ReadOptions)
        ?? throw new FormatException($"Empty energy-balance artifact '{path}'.");

    private sealed record Terms(
        double Demand, double Unserved, double Delivered, double Curtailment,
        double Charge, double Discharge, double Imports, double Exports, double TransmissionLoss,
        double SampleError = 0, int Technologies = 0)
    {
        public double Served => Demand - Unserved;
        public double Generation => Delivered + Charge + Curtailment;
        public double DeliveredError => SampleError * Technologies;
    }
}

internal sealed class PublishedSweepTheoryAttribute : TheoryAttribute
{
    public PublishedSweepTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                PublishedEnergyBalanceTests.SweepDirectoryVariable))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                PublishedEnergyBalanceTests.DataDirectoryVariable)))
        {
            Skip = $"Set {PublishedEnergyBalanceTests.SweepDirectoryVariable} and {PublishedEnergyBalanceTests.DataDirectoryVariable} to run the published-data acceptance.";
        }
    }
}
