using ClosedXML.Excel;
using NEM.CLI.Application;
using NEM.CLI.Infrastructure;
using System.IO.Compression;
using System.Text.Json;

namespace NEM.CLI.Tests.Ingest;

/// <summary>
/// Builds a throwaway input bundle on disk, with a solution root and CLI settings beside it, and
/// runs command lines against it through the real <see cref="CommandRouter"/>. Shared by the
/// bundle-scoped commands and the single-source imports that read the same bundle.
/// </summary>
internal sealed class BundleFixture : IDisposable
{
    private static readonly string[] Headers =
    [
        "AEMO Survey ID", "Site Name", "AEMO KCI ID", "Site Owner", "Custodian", "Region",
        "Max Site Capacity (AC)", "Gen Info Unit ID", "Unit Name", "Technology Type",
        "Technology Detail", "Gas Turbine Fuel Type", "DUID", "Dispatch Type", "Unit Count",
        "Unit Capacity (MW DC)", "Unit Capacity (MW AC)", "Agg Nameplate Capacity (MW DC)",
        "Agg Nameplate Capacity (MW AC)", "Agg Nameplate Storage Capacity (MWh)",
        "Commitment Status", "Full Commercial Use Date", "Expected Closure Year", "Closure Date",
        "Survey Last Requested Date", "Survey Latest Update Date",
    ];
    private readonly DateTimeOffset periodStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));
    private readonly string[] regions;
    private readonly int days;

    public BundleFixture(string[] regions, int days = 1)
    {
        this.regions = regions;
        this.days = days;
        RootPath = Path.Combine(Path.GetTempPath(), $"nemsim-ingest-{Guid.NewGuid():N}");
        BundlePath = Path.Combine(RootPath, "bundle");
        OutputRoot = Path.Combine(RootPath, "output");
        Directory.CreateDirectory(Path.Combine(BundlePath, "demand", "operational-demand-hh"));
        Directory.CreateDirectory(Path.Combine(BundlePath, "generation", "generation-information"));
        foreach (string region in regions)
        {
            string weatherPath = Path.Combine(BundlePath, "weather", region);
            Directory.CreateDirectory(weatherPath);
            File.WriteAllText(Path.Combine(weatherPath, "weather.epw"), BuildEpw());
        }
        WriteManifest();
        WriteGenerationWorkbook();
        foreach (string region in regions)
        {
            ReplaceDemandRows(region, days);
        }
        File.WriteAllText(Path.Combine(RootPath, "appsettings.local.json"), JsonSerializer.Serialize(new
        {
            inputBundleRoot = "bundle",
            outputRoot = "output",
            defaultScenarioPath = "unused.json",
        }));
        File.WriteAllText(Path.Combine(RootPath, "NemSim.slnx"), string.Empty);
        Paths = RepositoryPaths.Discover(RootPath);
    }

    public string RootPath { get; }
    public string BundlePath { get; }
    public string OutputRoot { get; }
    private RepositoryPaths Paths { get; }

    public void ReplaceDemandRows(string region, int rowDays)
    {
        string path = Path.Combine(BundlePath, "demand", "operational-demand-hh", $"{region}.zip");
        if (File.Exists(path)) File.Delete(path);
        string[] lines =
        [
            "C,NEMP.WORLD,ACTUAL_OPERATIONAL_DEMAND_HH,AEMO,PUBLIC",
            "I,OPERATIONAL_DEMAND,ACTUAL,3,INTERVAL_DATETIME,REGIONID,OPERATIONAL_DEMAND",
        ];
        var rows = Enumerable.Range(0, rowDays * 48).Select(index =>
            FormattableString.Invariant($"D,OPERATIONAL_DEMAND,ACTUAL,3,{periodStart.AddMinutes(30 * (index + 1)):yyyy/MM/dd HH:mm:ss},{region},100"));
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("demand.csv");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(string.Join(Environment.NewLine, lines.Concat(rows)));
    }

    public (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = new CommandRouter(Paths, RootPath, output, error).Run(args);
        return (exitCode, output.ToString(), error.ToString());
    }

    public void Dispose() => Directory.Delete(RootPath, recursive: true);

    private void WriteManifest() => File.WriteAllText(Path.Combine(BundlePath, "manifest.json"), JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        bundleId = "bundle",
        name = "Test bundle",
        period = new { start = periodStart, end = periodStart.AddDays(days) },
        regions,
    }));

    private void WriteGenerationWorkbook()
    {
        string path = Path.Combine(BundlePath, "generation", "generation-information", "generation.xlsx");
        using var workbook = new XLWorkbook();
        IXLWorksheet worksheet = workbook.AddWorksheet("Generator Information");
        for (int index = 0; index < Headers.Length; index++) worksheet.Cell(4, index + 1).Value = Headers[index];
        object?[] values = [2489, "Test unit", null, "Owner", "Custodian", "NSW1", 100, 248901,
            "Unit 1", "Battery Storage", null, null, "TEST1", "Scheduled", 1, 100, 100, 100,
            100, 200, "In Service", null, null, null, null, null];
        for (int index = 0; index < values.Length; index++)
        {
            if (values[index] is not null) worksheet.Cell(5, index + 1).Value = XLCellValue.FromObject(values[index]);
        }
        workbook.SaveAs(path);
    }

    private static string BuildEpw()
    {
        var lines = new List<string>
        {
            "LOCATION,Test City,NSW,AUS,TMY,123456,-33.5,151.2,10,100",
            "DESIGN CONDITIONS,0", "TYPICAL/EXTREME PERIODS,0", "GROUND TEMPERATURES,0",
            "HOLIDAYS/DAYLIGHT SAVING,No", "COMMENTS 1,Test", "COMMENTS 2,Test", "DATA PERIODS,1,1",
        };
        for (int index = 0; index < 8760; index++)
        {
            DateTime date = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddHours(index);
            string[] fields = Enumerable.Repeat("0", 22).ToArray();
            fields[0] = date.Year.ToString(); fields[1] = date.Month.ToString(); fields[2] = date.Day.ToString();
            fields[3] = (date.Hour + 1).ToString(); fields[5] = "A0"; fields[6] = "20";
            fields[13] = "100"; fields[14] = "100"; fields[15] = "50"; fields[21] = "3";
            lines.Add(string.Join(',', fields));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
