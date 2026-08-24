using System.Text;
using AwesomeAssertions;
using NEMSweep.Web.Services;

namespace NEMSweep.Web.Tests.Services;

public sealed class CsvWriterTests
{
    [Fact]
    public void Build_WritesHeadersThenRowsSeparatedByCarriageReturnLineFeed()
    {
        string csv = CsvWriter.Build(["Run", "Cost"], [["Baseline", "166.90"]]);

        csv.Should().Be("Run,Cost\r\nBaseline,166.90\r\n");
    }

    [Fact]
    public void Build_QuotesAFieldContainingASeparator()
    {
        string csv = CsvWriter.Build(["Label"], [["Storage grown, then held"]]);

        csv.Should().Be("Label\r\n\"Storage grown, then held\"\r\n");
    }

    [Fact]
    public void Build_DoublesQuotesInsideAQuotedField()
    {
        string csv = CsvWriter.Build(["Label"], [["The \"cheapest\" run, arguably"]]);

        csv.Should().Be("Label\r\n\"The \"\"cheapest\"\" run, arguably\"\r\n");
    }

    [Fact]
    public void Build_QuotesAFieldContainingANewline()
    {
        string csv = CsvWriter.Build(["Note"], [["First line\nsecond line"]]);

        csv.Should().Be("Note\r\n\"First line\nsecond line\"\r\n");
    }

    /// <summary>
    /// Failure messages come from the model and land in this file unmodified, so a value that a
    /// spreadsheet would treat as a formula must not be handed to one as written.
    /// </summary>
    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("@import", "'@import")]
    [InlineData("+SUM(A1:A9)", "'+SUM(A1:A9)")]
    [InlineData("-1+2", "'-1+2")]
    [InlineData("+500 MW", "'+500 MW")]
    public void Build_NeutralisesAFieldASpreadsheetWouldTreatAsAFormula(string field, string expected)
    {
        string csv = CsvWriter.Build(["Label"], [[field]]);

        csv.Should().Be($"Label\r\n{expected}\r\n");
    }

    /// <summary>
    /// A signed number has to arrive as a number or the column cannot be summed, which is the whole
    /// point of exporting it. Only signed values that are not numbers are expressions.
    /// </summary>
    [Theory]
    [InlineData("-266101")]
    [InlineData("-266101.5")]
    [InlineData("+0.3798")]
    [InlineData("-1.5E-08")]
    public void Build_LeavesASignedNumberNumeric(string field)
    {
        CsvWriter.Build(["Value"], [[field]]).Should().Be($"Value\r\n{field}\r\n");
    }

    [Fact]
    public void Build_WritesAnEmptyFieldForAMissingValue()
    {
        string csv = CsvWriter.Build(["A", "B"], [["", "1"]]);

        csv.Should().Be("A,B\r\n,1\r\n");
    }

    [Fact]
    public void ToDataUri_LeadsWithAByteOrderMarkSoSpreadsheetsReadItAsUtf8()
    {
        string uri = CsvWriter.ToDataUri("Region\r\nVictoria\r\n");

        uri.Should().StartWith("data:text/csv;charset=utf-8;base64,");
        byte[] decoded = Convert.FromBase64String(uri["data:text/csv;charset=utf-8;base64,".Length..]);
        decoded.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        Encoding.UTF8.GetString(decoded[3..]).Should().Be("Region\r\nVictoria\r\n");
    }

    [Theory]
    [InlineData(new[] { "datacentre-nameplate-nsw1-fy2026", "system", "runs" }, "datacentre-nameplate-nsw1-fy2026-system-runs.csv")]
    [InlineData(new[] { "regions", "01 Jul 2025 to 30 Jun 2026" }, "regions-01-jul-2025-to-30-jun-2026.csv")]
    public void FileName_ReducesPartsToSomethingABrowserWillAccept(string[] parts, string expected)
    {
        CsvWriter.FileName(parts).Should().Be(expected);
    }

    [Fact]
    public void FileName_FallsBackWhenNoPartSurvives()
    {
        CsvWriter.FileName("", "  ").Should().Be("export.csv");
    }
}
