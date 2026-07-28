using ClosedXML.Excel;
using NEM.Contracts;
using System.Globalization;

namespace NEM.CLI;

internal static class GenerationInformationParser
{
    private const string WorksheetName = "Generator Information";
    private const int HeaderRow = 4;
    private static readonly string[] ExpectedHeaders =
    [
        "AEMO Survey ID",
        "Site Name",
        "AEMO KCI ID",
        "Site Owner",
        "Custodian",
        "Region",
        "Max Site Capacity (AC)",
        "Gen Info Unit ID",
        "Unit Name",
        "Technology Type",
        "Technology Detail",
        "Gas Turbine Fuel Type",
        "DUID",
        "Dispatch Type",
        "Unit Count",
        "Unit Capacity (MW DC)",
        "Unit Capacity (MW AC)",
        "Agg Nameplate Capacity (MW DC)",
        "Agg Nameplate Capacity (MW AC)",
        "Agg Nameplate Storage Capacity (MWh)",
        "Commitment Status",
        "Full Commercial Use Date",
        "Expected Closure Year",
        "Closure Date",
        "Survey Last Requested Date",
        "Survey Latest Update Date",
    ];

    public static IReadOnlyList<GenerationInformationRow> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Generation-information workbook was not found.", path);
        }

        using var workbook = new XLWorkbook(path);
        if (!workbook.TryGetWorksheet(WorksheetName, out IXLWorksheet? worksheet))
        {
            throw new FormatException($"Workbook is missing worksheet '{WorksheetName}'.");
        }

        ValidateHeaders(worksheet);
        int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? HeaderRow;
        var rows = new List<GenerationInformationRow>();
        var unitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int rowNumber = HeaderRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            IXLRow row = worksheet.Row(rowNumber);
            if (Enumerable.Range(1, ExpectedHeaders.Length).All(column => row.Cell(column).IsEmpty()))
            {
                continue;
            }

            string unitId = RequiredText(row, 8, rowNumber);
            if (!unitIds.Add(unitId))
            {
                throw new FormatException($"Row {rowNumber}: duplicate Gen Info Unit ID '{unitId}'.");
            }

            rows.Add(new GenerationInformationRow(
                RequiredText(row, 1, rowNumber),
                RequiredText(row, 2, rowNumber),
                Text(row.Cell(3)),
                Text(row.Cell(4)),
                Text(row.Cell(5)),
                RequiredText(row, 6, rowNumber),
                Number(row.Cell(7), rowNumber),
                unitId,
                Text(row.Cell(9)),
                RequiredText(row, 10, rowNumber),
                Text(row.Cell(11)),
                Text(row.Cell(12)),
                Text(row.Cell(13)),
                Text(row.Cell(14)),
                Number(row.Cell(15), rowNumber),
                Number(row.Cell(16), rowNumber),
                Number(row.Cell(17), rowNumber),
                Number(row.Cell(18), rowNumber),
                Number(row.Cell(19), rowNumber),
                Number(row.Cell(20), rowNumber),
                RequiredText(row, 21, rowNumber),
                Date(row.Cell(22), rowNumber),
                Integer(row.Cell(23), rowNumber),
                Date(row.Cell(24), rowNumber),
                Date(row.Cell(25), rowNumber),
                Date(row.Cell(26), rowNumber)));
        }

        return rows;
    }

    private static void ValidateHeaders(IXLWorksheet worksheet)
    {
        for (int index = 0; index < ExpectedHeaders.Length; index++)
        {
            string actual = worksheet.Cell(HeaderRow, index + 1).GetString().Trim();
            if (!actual.Equals(ExpectedHeaders[index], StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"Cell {worksheet.Cell(HeaderRow, index + 1).Address}: expected header "
                    + $"'{ExpectedHeaders[index]}', found '{actual}'.");
            }
        }
    }

    private static string RequiredText(IXLRow row, int column, int rowNumber)
    {
        return Text(row.Cell(column))
            ?? throw new FormatException(
                $"Row {rowNumber}: {ExpectedHeaders[column - 1]} is required.");
    }

    private static string? Text(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        string value = cell.DataType switch
        {
            XLDataType.Number => cell.GetDouble().ToString("0.################", CultureInfo.InvariantCulture),
            XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => cell.GetString().Trim(),
        };
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static double? Number(IXLCell cell, int rowNumber)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.TryGetValue(out double value) && double.IsFinite(value))
        {
            return value;
        }

        throw InvalidCell(cell, rowNumber, "a number");
    }

    private static int? Integer(IXLCell cell, int rowNumber)
    {
        double? value = Number(cell, rowNumber);
        if (value is null)
        {
            return null;
        }

        double nonNullableValue = value.Value;
        if (nonNullableValue >= int.MinValue
            && nonNullableValue <= int.MaxValue
            && nonNullableValue == Math.Truncate(nonNullableValue))
        {
            return (int)nonNullableValue;
        }

        throw InvalidCell(cell, rowNumber, "a whole number");
    }

    private static DateOnly? Date(IXLCell cell, int rowNumber)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.TryGetValue(out DateTime value))
        {
            return DateOnly.FromDateTime(value);
        }

        throw InvalidCell(cell, rowNumber, "an Excel date");
    }

    private static FormatException InvalidCell(IXLCell cell, int rowNumber, string expected)
    {
        return new FormatException(
            $"Row {rowNumber}, column {cell.Address.ColumnLetter}: expected {expected}, "
            + $"found '{cell.GetFormattedString()}'.");
    }
}