using System.Text;

namespace NEM.Web.Services;

/// <summary>
/// Builds a CSV file from rows already formatted for display, and packages it as a data URI so a
/// download needs no script and no server. Analysts asked of this site will want the numbers in a
/// spreadsheet; a table they have to retype is a table they will retype wrongly.
/// </summary>
public static class CsvWriter
{
    /// <summary>
    /// Characters that make a spreadsheet treat a text cell as a formula. A run label such as
    /// "=SUM(A1)" would execute on open, so a leading one is neutralised with an apostrophe. Plus
    /// and minus are left alone: they lead ordinary values here, and neither begins a formula
    /// without a valid expression after it.
    /// </summary>
    private static readonly char[] FormulaStarters = ['=', '@', '\t', '\r'];

    public static string Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendRow(builder, headers);
        foreach (IReadOnlyList<string> row in rows)
        {
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The file as a data URI an anchor can carry. A byte-order mark is included because
    /// spreadsheets otherwise read a UTF-8 file as the local codepage and mangle any non-ASCII
    /// character in it.
    /// </summary>
    public static string ToDataUri(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];
        return $"data:text/csv;charset=utf-8;base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>A file name safe to hand a browser, derived from what the table is of.</summary>
    public static string FileName(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        IEnumerable<string> cleaned = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => new string([.. part.Select(character =>
                char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')]))
            .Select(part => part.Trim('-'))
            .Where(part => part.Length > 0);
        string name = string.Join("-", cleaned).Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrEmpty(name) ? "export.csv" : $"{name}.csv";
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> row)
    {
        for (int index = 0; index < row.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(row[index]));
        }

        // Carriage return and line feed together, which is what the CSV convention specifies and
        // what spreadsheets on every platform read without complaint.
        builder.Append("\r\n");
    }

    private static string Escape(string? field)
    {
        string value = field ?? string.Empty;
        if (value.Length > 0 && FormulaStarters.Contains(value[0]))
        {
            value = "'" + value;
        }

        if (!value.Contains(',', StringComparison.Ordinal)
            && !value.Contains('"', StringComparison.Ordinal)
            && !value.Contains('\n', StringComparison.Ordinal)
            && !value.Contains('\r', StringComparison.Ordinal))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
