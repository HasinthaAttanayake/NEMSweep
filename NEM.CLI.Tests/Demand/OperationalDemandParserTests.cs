using AwesomeAssertions;
using NEM.CLI.Demand;
using System.Globalization;
using System.IO.Compression;

namespace NEM.CLI.Tests.Demand;

public sealed class OperationalDemandParserTests
{
    private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);
    private static readonly DateTimeOffset PeriodStart =
        new(2025, 7, 1, 0, 0, 0, NemOffset);

    [Fact]
    public void Read_RecursesAndBuildsIntervalBeginningSeries()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddFullYear("NSW1", nested: true);

        OperationalDemandData result = OperationalDemandParser.Read(
            [fixture.PathFor("full-year.zip")], ["NSW1"], PeriodStart, PeriodStart.AddYears(1))["NSW1"];

        result.Demand.Start.Should().Be(PeriodStart);
        result.Demand.Resolution.Should().Be(TimeSpan.FromMinutes(30));
        result.Demand.Length.Should().Be(17_520);
        result.Demand.InstantAt(0).Should().Be(PeriodStart);
        result.Demand[0].Megawatts.Should().Be(1_000);
        result.Demand[^1].Megawatts.Should().Be(18_519);
    }

    [Fact]
    public void Read_FloorsNegativeOperationalDemandToZero()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddRows(
            "sa1.zip",
            [(PeriodStart + TimeSpan.FromMinutes(30), "SA1", -14)]);

        OperationalDemandData result = OperationalDemandParser.Read(
            [fixture.PathFor("sa1.zip")], ["SA1"], PeriodStart, PeriodStart.AddMinutes(30))["SA1"];

        result.Demand[0].Megawatts.Should().Be(0);
    }

    [Fact]
    public void Read_ReportsClampedIntervalCountForRegion()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddRows(
            "sa1.zip",
            [
                (PeriodStart + TimeSpan.FromMinutes(30), "SA1", -14),
                (PeriodStart + TimeSpan.FromHours(1), "SA1", 500),
            ]);

        OperationalDemandData result = OperationalDemandParser.Read(
            [fixture.PathFor("sa1.zip")], ["SA1"], PeriodStart, PeriodStart.AddHours(1))["SA1"];

        result.ClampedIntervals.Should().Be(1);
    }

    [Fact]
    public void Read_RejectsConflictingNegativeReadingsRatherThanAgreeingAfterClamping()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddRows(
            "first.zip",
            [(PeriodStart + TimeSpan.FromMinutes(30), "SA1", -14)]);
        fixture.AddRows(
            "second.zip",
            [(PeriodStart + TimeSpan.FromMinutes(30), "SA1", -3)]);

        var act = () => OperationalDemandParser.Read(
            [fixture.PathFor("first.zip"), fixture.PathFor("second.zip")],
            ["SA1"], PeriodStart, PeriodStart.AddMinutes(30));

        act.Should().Throw<OperationalDemandDataQualityException>()
            .WithMessage("*Conflicting operational demand*SA1*");
    }

    [Fact]
    public void Read_DropsIdenticalOverlapsAcrossHeaderLayouts()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddFullYear("NSW1", nested: true);
        fixture.AddRows(
            "duplicate.zip",
            [(PeriodStart + TimeSpan.FromMinutes(30), "NSW1", 1_000)],
            intervalFirst: false);

        OperationalDemandData result = OperationalDemandParser.Read(
            [fixture.PathFor("full-year.zip"), fixture.PathFor("duplicate.zip")],
            ["NSW1"], PeriodStart, PeriodStart.AddYears(1))["NSW1"];

        result.Demand.Length.Should().Be(17_520);
        result.Demand[0].Megawatts.Should().Be(1_000);
    }

    [Fact]
    public void Read_RejectsConflictingOverlapWithBothSources()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddRows(
            "first.zip",
            [(PeriodStart + TimeSpan.FromMinutes(30), "NSW1", 1_000)]);
        fixture.AddRows(
            "conflict.zip",
            [(PeriodStart + TimeSpan.FromMinutes(30), "NSW1", 1_001)]);

        var act = () => OperationalDemandParser.Read(
            [fixture.PathFor("first.zip"), fixture.PathFor("conflict.zip")],
            ["NSW1"], PeriodStart, PeriodStart.AddMinutes(30));

        OperationalDemandDataQualityException exception = act.Should()
            .Throw<OperationalDemandDataQualityException>()
            .WithMessage("*Conflicting operational demand*NSW1*")
            .Which;
        exception.Message.Should().Contain("1000 MW");
        exception.Message.Should().Contain("1001 MW");
        exception.Message.Should().Contain("first.zip");
        exception.Message.Should().Contain("conflict.zip");
    }

    [Fact]
    public void Read_RejectsMissingIntervalNamingRegionAndInstant()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddRows(
            "partial.zip",
            [(PeriodStart + TimeSpan.FromMinutes(30), "NSW1", 1_000)]);

        var act = () => OperationalDemandParser.Read(
            [fixture.PathFor("partial.zip")], ["NSW1"], PeriodStart, PeriodStart.AddHours(1));

        act.Should().Throw<OperationalDemandDataQualityException>()
            .WithMessage("*NSW1*missing interval*2025-07-01T00:30:00*");
    }

    [Fact]
    public void Read_ReturnsEveryRequestedRegionAndOmitsUndeclaredRows()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddRows("regions.zip", [
            (PeriodStart + TimeSpan.FromMinutes(30), "NSW1", 1_000),
            (PeriodStart + TimeSpan.FromHours(1), "NSW1", 1_001),
            (PeriodStart + TimeSpan.FromMinutes(30), "QLD1", 2_000),
            (PeriodStart + TimeSpan.FromHours(1), "QLD1", 2_001),
            (PeriodStart + TimeSpan.FromMinutes(30), "VIC1", 3_000),
        ]);

        IReadOnlyDictionary<string, OperationalDemandData> result = OperationalDemandParser.Read(
            [fixture.PathFor("regions.zip")], ["NSW1", "QLD1"], PeriodStart, PeriodStart.AddHours(1));

        result.Keys.Should().BeEquivalentTo(["NSW1", "QLD1"]);
        result["NSW1"].Demand[^1].Megawatts.Should().Be(1_001);
        result["QLD1"].Demand[^1].Megawatts.Should().Be(2_001);
        result.ContainsKey("VIC1").Should().BeFalse();
    }

    [Fact]
    public void Read_UsesExplicitEndAndExcludesRowsAtEnd()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddRows("bounded.zip", [
            (PeriodStart + TimeSpan.FromMinutes(30), "NSW1", 1_000),
            (PeriodStart + TimeSpan.FromHours(1), "NSW1", 1_001),
            (PeriodStart + TimeSpan.FromMinutes(90), "NSW1", 1_002),
        ]);

        OperationalDemandData result = OperationalDemandParser.Read(
            [fixture.PathFor("bounded.zip")], ["NSW1"], PeriodStart, PeriodStart.AddHours(1))["NSW1"];

        result.Demand.Length.Should().Be(2);
        result.Demand[^1].Megawatts.Should().Be(1_001);
    }

    [Fact]
    public void Read_DeduplicatesRepeatedArchivePaths()
    {
        using var fixture = new DemandArchiveFixture();
        fixture.AddRows("single.zip", [
            (PeriodStart + TimeSpan.FromMinutes(30), "NSW1", 1_000),
        ]);

        OperationalDemandData result = OperationalDemandParser.Read(
            [fixture.PathFor("single.zip"), fixture.PathFor("single.zip")],
            ["NSW1"], PeriodStart, PeriodStart.AddMinutes(30))["NSW1"];

        result.SourceArchives.Should().ContainSingle().Which.Should().Be("single.zip");
    }

    private sealed class DemandArchiveFixture : IDisposable
    {
        public DemandArchiveFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"nemsim-demand-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string PathFor(string archiveName) => Path.Combine(DirectoryPath, archiveName);

        public void AddFullYear(string region, bool nested)
        {
            var rows = Enumerable.Range(0, 17_520)
                .Select(index => (
                    PeriodStart + TimeSpan.FromMinutes(30 * (index + 1L)),
                    region,
                    1_000d + index));
            AddRows("full-year.zip", rows, nested: nested);
        }

        public void AddRows(
            string archiveName,
            IEnumerable<(DateTimeOffset IntervalEnd, string Region, double Megawatts)> rows,
            bool intervalFirst = true,
            bool nested = false)
        {
            string csv = BuildCsv(rows, intervalFirst);
            string archivePath = PathFor(archiveName);
            using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            if (nested)
            {
                ZipArchiveEntry nestedEntry = archive.CreateEntry("nested.zip");
                using Stream nestedStream = nestedEntry.Open();
                using var nestedBuffer = new MemoryStream();
                using (var nestedArchive = new ZipArchive(nestedBuffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteEntry(nestedArchive, "demand.csv", csv);
                }

                nestedBuffer.Position = 0;
                nestedBuffer.CopyTo(nestedStream);
            }
            else
            {
                WriteEntry(archive, "demand.csv", csv);
            }
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        private static string BuildCsv(
            IEnumerable<(DateTimeOffset IntervalEnd, string Region, double Megawatts)> rows,
            bool intervalFirst)
        {
            var lines = new List<string>
            {
                "C,NEMP.WORLD,ACTUAL_OPERATIONAL_DEMAND_HH,AEMO,PUBLIC",
                intervalFirst
                    ? "I,OPERATIONAL_DEMAND,ACTUAL,3,INTERVAL_DATETIME,REGIONID,OPERATIONAL_DEMAND"
                    : "I,OPERATIONAL_DEMAND,ACTUAL,3,REGIONID,INTERVAL_DATETIME,OPERATIONAL_DEMAND",
            };

            lines.AddRange(rows.Select(row => intervalFirst
                ? FormattableString.Invariant(
                    $"D,OPERATIONAL_DEMAND,ACTUAL,3,{row.IntervalEnd:yyyy/MM/dd HH:mm:ss},{row.Region},{row.Megawatts}")
                : FormattableString.Invariant(
                    $"D,OPERATIONAL_DEMAND,ACTUAL,3,{row.Region},{row.IntervalEnd:yyyy/MM/dd HH:mm:ss},{row.Megawatts}")));
            lines.Add("C,END OF REPORT");
            return string.Join(Environment.NewLine, lines);
        }

        private static void WriteEntry(ZipArchive archive, string name, string contents)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(contents);
        }
    }
}