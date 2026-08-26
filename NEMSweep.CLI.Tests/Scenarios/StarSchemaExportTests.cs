using AwesomeAssertions;
using NEMSweep.CLI.Scenarios;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Tests.Scenarios;

/// <summary>
/// The star schema is what a reader actually opens, so these cover the shape a consumer joins on
/// rather than the numbers, which the dispatch tests already own.
/// </summary>
public sealed class StarSchemaExportTests
{
    /// <summary>The ceiling every mainstream spreadsheet shares, and truncates past without erroring.</summary>
    private const int SpreadsheetRowLimit = 1_048_576;

    [Fact]
    public void Write_KeysFactRowsByRegionIdRatherThanPublishedFileName()
    {
        Dictionary<string, string> tables = WriteTables();

        Rows(tables["fact_scalars.csv"])[1][1].Should().Be("NSW1");
        Rows(tables["fact_dispatch.csv"])[1][1].Should().Be("NSW1");
        tables["fact_scalars.csv"].Should().NotContain(".json");
    }

    [Fact]
    public void Write_NumbersHoursFromOneSoTheAxisIsUnambiguous()
    {
        Dictionary<string, string> tables = WriteTables();

        Rows(tables["dim_time.csv"])[1][0].Should().Be("1");
        Rows(tables["fact_dispatch.csv"])[1][2].Should().Be("1");
    }

    [Fact]
    public void Write_UnpivotsTechnologyIntoADimensionRatherThanAColumnEach()
    {
        Dictionary<string, string> tables = WriteTables();

        string[] header = Rows(tables["fact_generation.csv"])[0];
        header.Should().Equal("pointId", "regionId", "hourIndex", "technology", "deliveredMw");
        Rows(tables["fact_generation.csv"]).Skip(1).Select(row => row[3])
            .Should().OnlyContain(technology => technology == "Gas");
    }

    [Fact]
    public void Write_TakesScalarColumnsAndTheirLabelsFromThePublishedCatalogue()
    {
        Dictionary<string, string> tables = WriteTables();

        string[] expected = [.. SweepScalarCatalog.Descriptors.Select(descriptor => descriptor.Name)];
        Rows(tables["fact_scalars.csv"])[0].Skip(2).Should().Equal(expected);
        Rows(tables["dim_scalar.csv"]).Skip(1).Select(row => row[0]).Should().Equal(expected);
    }

    [Fact]
    public void Write_StampsEveryFactRowWithThePointSoAFolderOfRunsConcatenates()
    {
        Dictionary<string, string> tables = WriteTables(pointId: "p7");

        foreach ((string name, string contents) in tables.Where(table => table.Key.StartsWith("fact_")))
        {
            Rows(contents)[0][0].Should().Be("pointId", $"{name} must carry the point key");
            Rows(contents).Skip(1).Select(row => row[0])
                .Should().OnlyContain(value => value == "p7", $"{name} rows belong to one point");
        }
    }

    [Fact]
    public void Write_LocatesARegionEvenWhenTheRunHasNoInterconnectors()
    {
        Dictionary<string, string> tables = WriteTables();

        // The published artifact only exposes coordinates on interconnector endpoints, so reading
        // them from there leaves a single-region run blank, and that is the first run anyone makes.
        string[] region = Rows(tables["dim_region.csv"])[1];
        region[0].Should().Be("NSW1");
        region[1].Should().NotBeEmpty();
        region[2].Should().NotBeEmpty();
        double.Parse(region[1], System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(-33.9, 0.01);
    }

    [Fact]
    public void Write_KeepsEveryTableInsideTheSpreadsheetRowCeiling()
    {
        Dictionary<string, string> tables = WriteTables();

        foreach ((string name, string contents) in tables)
        {
            Rows(contents).Count.Should().BeLessThan(
                SpreadsheetRowLimit,
                $"{name} is silently truncated past that, not rejected");
        }
    }

    [Fact]
    public void Write_QuotesOnlyValuesThatWouldOtherwiseBreakTheRow()
    {
        Dictionary<string, string> tables = WriteTables();

        // Labels are the only free text emitted, and one of them contains a comma-free bracket pair;
        // the rows must still parse into the declared column count either way.
        foreach (string[] row in Rows(tables["dim_scalar.csv"]))
        {
            row.Should().HaveCount(4);
        }

        tables["fact_dispatch.csv"].Should().NotContain("\"");
    }

    private static Dictionary<string, string> WriteTables(string pointId = "scenario")
    {
        using var fixture = new StarSchemaFixture();
        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        StarSchemaExport.Write(
            fixture.Publication,
            fixture.PowerSystem,
            fixture.Directory,
            pointId,
            (path, contents) => written[Path.GetFileName(path)] = contents);
        return written;
    }

    [Fact]
    public void WritePointDimension_KeepsTheAxisValueThePublishedIndexCarries()
    {
        var written = new Dictionary<string, string>(StringComparer.Ordinal);

        StarSchemaExport.WritePointDimension(
            [Point("p0", 0.1234567), Point("p1", 0.1234568)],
            Path.GetTempPath(),
            (path, contents) => written[Path.GetFileName(path)] = contents);

        List<string[]> rows = Rows(written["dim_point.csv"]);
        rows[1][2].Should().Be("0.1234567");
        rows[2][2].Should().Be("0.1234568");
    }

    [Fact]
    public void PruneStalePointFacts_RemovesFactsNoPointInThisRunProduced()
    {
        using var fixture = new StarSchemaFixture();
        string points = Path.Combine(fixture.Directory, "points");
        Directory.CreateDirectory(Path.Combine(points, "p0"));
        Directory.CreateDirectory(Path.Combine(points, "p1"));
        File.WriteAllText(Path.Combine(points, "p1", "fact_dispatch.csv"), "stale");

        StarSchemaExport.PruneStalePointFacts(["p0"], fixture.Directory);

        Directory.Exists(Path.Combine(points, "p0")).Should().BeTrue();
        Directory.Exists(Path.Combine(points, "p1")).Should().BeFalse();
    }

    [Fact]
    public void PruneStalePointFacts_IsQuietWhenNoFactsHaveBeenWritten()
    {
        using var fixture = new StarSchemaFixture();

        Action prune = () => StarSchemaExport.PruneStalePointFacts(["p0"], fixture.Directory);

        prune.Should().NotThrow();
    }

    private static SweepIndexPointDTO Point(string pointId, double axisValue) =>
        new(
            pointId,
            pointId.ToUpperInvariant(),
            axisValue,
            SweepPointStatus.Succeeded,
            $"points/{pointId}.json",
            $"configs/{pointId}.json",
            Scalars: null,
            Reliability: null,
            StorageSizing: null,
            IntervalPointers: null,
            Failure: null);

    /// <summary>Splits a written table back into rows and columns, honouring quoted fields.</summary>
    private static List<string[]> Rows(string contents) =>
        [.. contents.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(SplitRow)];

    private static string[] SplitRow(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character is '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] is '"')
                {
                    field.Append('"');
                    index++;
                    continue;
                }

                quoted = !quoted;
                continue;
            }

            if (character is ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            field.Append(character);
        }

        fields.Add(field.ToString());
        return [.. fields];
    }
}
