using ClosedXML.Excel;
using AwesomeAssertions;
using NEMSweep.CLI.Generation;

namespace NEMSweep.CLI.Tests.Generation;

public sealed class GenerationInformationParserTests
{
    private static readonly string[] Headers =
    [
        "AEMO Survey ID", "Site Name", "AEMO KCI ID", "Site Owner", "Custodian",
        "Region", "Max Site Capacity (AC)", "Gen Info Unit ID", "Unit Name",
        "Technology Type", "Technology Detail", "Gas Turbine Fuel Type", "DUID",
        "Dispatch Type", "Unit Count", "Unit Capacity (MW DC)", "Unit Capacity (MW AC)",
        "Agg Nameplate Capacity (MW DC)", "Agg Nameplate Capacity (MW AC)",
        "Agg Nameplate Storage Capacity (MWh)", "Commitment Status",
        "Full Commercial Use Date", "Expected Closure Year", "Closure Date",
        "Survey Last Requested Date", "Survey Latest Update Date",
    ];

    [Fact]
    public void Read_ParsesColumnsThroughZAndIgnoresSeasonalCapacity()
    {
        using var fixture = new GenerationWorkbookFixture();
        fixture.AddValidRow();
        fixture.Worksheet.Cell(4, 27).Value = "Scheduled Capacity (MW) Winter 2026";
        fixture.Worksheet.Cell(5, 27).Value = "not parsed";
        fixture.Save();

        var result = GenerationInformationParser.Read(fixture.Path);

        result.Should().ContainSingle();
        result[0].AemoSurveyId.Should().Be("2489");
        result[0].UnitCount.Should().Be(5.15);
        result[0].AggregateNameplateCapacityAcMw.Should().Be(127.38);
        result[0].FullCommercialUseDate.Should().Be(new DateOnly(2029, 12, 28));
        result[0].SurveyLatestUpdateDate.Should().Be(new DateOnly(2026, 4, 1));
    }

    [Fact]
    public void Read_RejectsChangedRequiredHeader()
    {
        using var fixture = new GenerationWorkbookFixture();
        fixture.Worksheet.Cell(4, 10).Value = "Technology";
        fixture.Save();

        var act = () => GenerationInformationParser.Read(fixture.Path);

        act.Should().Throw<FormatException>()
            .WithMessage("*J4*Technology Type*Technology*");
    }

    private sealed class GenerationWorkbookFixture : IDisposable
    {
        private readonly XLWorkbook _workbook = new();

        public GenerationWorkbookFixture()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"nemsweep-generation-{Guid.NewGuid():N}.xlsx");
            Worksheet = _workbook.AddWorksheet("Generator Information");
            for (int index = 0; index < Headers.Length; index++)
            {
                Worksheet.Cell(4, index + 1).Value = Headers[index];
            }
        }

        public string Path { get; }

        public IXLWorksheet Worksheet { get; }

        public void AddValidRow()
        {
            object?[] values =
            [
                2489, "Abermain BESS", null, "Owner", "Custodian", "QLD1", 125,
                248901, "Unit 1", "Battery Storage", "Lithium-ion", null, "ABER1",
                "Scheduled", 5.15, 127.38, 127.38, 127.38, 127.38, 254.1,
                "In Service", new DateTime(2029, 12, 28), 2049, null,
                new DateTime(2026, 3, 1), new DateTime(2026, 4, 1),
            ];
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] is not null)
                {
                    Worksheet.Cell(5, index + 1).Value = XLCellValue.FromObject(values[index]);
                }
            }
        }

        public void Save()
        {
            _workbook.SaveAs(Path);
        }

        public void Dispose()
        {
            _workbook.Dispose();
            File.Delete(Path);
        }
    }
}