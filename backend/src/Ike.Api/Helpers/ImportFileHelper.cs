using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualBasic.FileIO;

namespace Ike.Api.Helpers;

public record ImportTableRow(int RowNumber, Dictionary<string, string> Values);

public static class ImportFileHelper
{
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx"
    };

    public static string NormalizeHeader(string value) =>
        new(value.Trim().Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static async Task<List<ImportTableRow>> ReadRowsAsync(IFormFile file, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName);
        if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return ReadExcelRows(file);
        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return await ReadCsvRowsAsync(file, ct);
        throw new InvalidOperationException("Unsupported import file type.");
    }

    public static byte[] CreateCsvTemplate(IEnumerable<string> headers)
    {
        var line = string.Join(",", headers.Select(EscapeCsv));
        return Encoding.UTF8.GetBytes(line + Environment.NewLine);
    }

    public static byte[] CreateCsv(IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(value => EscapeCsv(value ?? string.Empty))));
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] CreateXlsxTemplate(IEnumerable<string> headers, string worksheetName, IReadOnlyDictionary<string, string[]>? dropdownsByHeader = null)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(worksheetName);
        IXLWorksheet? validationWs = null;
        var col = 1;
        var headerList = headers.ToList();
        foreach (var header in headerList)
        {
            ws.Cell(1, col).Value = header;
            ws.Cell(1, col).Style.Font.Bold = true;
            if (dropdownsByHeader != null && dropdownsByHeader.TryGetValue(header, out var options) && options.Length > 0)
            {
                validationWs ??= workbook.Worksheets.Add("_Validation");
                var validationCol = validationWs.LastColumnUsed()?.ColumnNumber() + 1 ?? 1;
                for (var i = 0; i < options.Length; i++)
                {
                    validationWs.Cell(i + 1, validationCol).Value = options[i];
                }
                var validationRange = validationWs.Range(1, validationCol, options.Length, validationCol);
                var rangeName = $"{worksheetName}_{NormalizeHeader(header)}_options";
                workbook.DefinedNames.Add(rangeName, validationRange);
                var range = ws.Range(2, col, 1000, col);
                var validation = range.CreateDataValidation();
                validation.List($"={rangeName}");
                validation.IgnoreBlanks = true;
                validation.InCellDropdown = true;
            }
            col++;
        }
        if (validationWs != null)
            validationWs.Visibility = XLWorksheetVisibility.VeryHidden;
        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static List<ImportTableRow> ReadExcelRows(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();
        var firstRow = ws.FirstRowUsed();
        if (firstRow == null)
            return new List<ImportTableRow>();

        var headers = firstRow.CellsUsed()
            .Select((cell, index) => new { Column = index + 1, Header = NormalizeHeader(cell.GetString()) })
            .Where(x => x.Header.Length > 0)
            .ToList();

        var rows = new List<ImportTableRow>();
        foreach (var row in ws.RowsUsed().Where(r => r.RowNumber() > firstRow.RowNumber()))
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var hasValue = false;
            foreach (var header in headers)
            {
                var value = row.Cell(header.Column).GetFormattedString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    hasValue = true;
                values[header.Header] = value;
            }
            if (hasValue)
                rows.Add(new ImportTableRow(row.RowNumber(), values));
        }
        return rows;
    }

    private static async Task<List<ImportTableRow>> ReadCsvRowsAsync(IFormFile file, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(output, ct);
            }

            using var parser = new TextFieldParser(tempPath, Encoding.UTF8)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true
            };
            parser.SetDelimiters(",");
            if (parser.EndOfData)
                return new List<ImportTableRow>();

            var rawHeaders = parser.ReadFields() ?? Array.Empty<string>();
            var headers = rawHeaders.Select(ImportFileHelper.NormalizeHeader).ToList();
            var rows = new List<ImportTableRow>();
            while (!parser.EndOfData)
            {
                var rowNumber = (int)parser.LineNumber;
                var fields = parser.ReadFields() ?? Array.Empty<string>();
                if (fields.All(string.IsNullOrWhiteSpace))
                    continue;

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Count && i < fields.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(headers[i]))
                        values[headers[i]] = fields[i].Trim();
                }
                rows.Add(new ImportTableRow(rowNumber, values));
            }
            return rows;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
