using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Gateway.Services.Reporting;

/// <summary>
/// Feedback id=95 — the roster should also come out as Excel, so the school can
/// keep working with the numbers rather than just print them.
///
/// <para>Values are written as inline strings. A shared-string table would be
/// smaller, but a roster is a few thousand short cells and inline strings keep
/// the writer trivially correct — no index to keep in step with the sheet.</para>
///
/// <para>Everything is text on purpose. Student codes and citizen IDs are digit
/// strings with meaning in their leading zeros; letting Excel treat them as
/// numbers would silently destroy them.</para>
/// </summary>
public sealed class TableXlsxGenerator
{
    public MemoryStream Generate(
        string title,
        string subtitle,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            sheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(sheetPart),
                SheetId = 1U,
                // Excel rejects sheet names over 31 chars or containing []:*?/\
                Name = SafeSheetName(title),
            });

            sheetData.AppendChild(RowOf(new[] { title }));
            if (!string.IsNullOrWhiteSpace(subtitle))
                sheetData.AppendChild(RowOf(new[] { subtitle }));
            sheetData.AppendChild(new Row());       // spacer
            sheetData.AppendChild(RowOf(headers));

            foreach (var r in rows)
            {
                var padded = new string[headers.Count];
                for (var i = 0; i < headers.Count; i++) padded[i] = i < r.Count ? r[i] : string.Empty;
                sheetData.AppendChild(RowOf(padded));
            }

            workbookPart.Workbook.Save();
        }

        ms.Position = 0;
        // OpenXml 3.2.0 writes the package root relationship with an absolute
        // target for spreadsheets too — verified against production output:
        // Target="/xl/workbook.xml". Same defect, same fix as the Word path
        // (feedback id=41); the normaliser is format-agnostic.
        return (MemoryStream)OpenXmlPackageNormalizer.Normalize(ms);
    }

    private static Row RowOf(IReadOnlyList<string> values)
    {
        var row = new Row();
        foreach (var v in values)
        {
            row.AppendChild(new Cell
            {
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(v ?? string.Empty)),
            });
        }
        return row;
    }

    private static string SafeSheetName(string s)
    {
        var cleaned = new string((s ?? "รายงาน").Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "รายงาน";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
