using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Gateway.Services.Reporting;

/// <summary>
/// Feedback id=80 / id=83 — "เพิ่มเครื่องปริ้น pdf/doc" on the student roster and
/// the homeroom-advisor page, asked by two different people.
///
/// <para>Both pages are plain tables, so rather than write a bespoke generator
/// per page this renders any (title, subtitle, headers, rows) into an A4 Word
/// document with a Thai-legible font. Callers own the data shaping; this owns
/// the document.</para>
///
/// <para>PDF is deliberately not produced here. LibreOffice lives only in the
/// BudgetApi image, and adding a ~500&#160;MB office suite to the API gateway to
/// flatten a table would be a poor trade — the browser's own print dialog
/// prints these tables to PDF, and that is what the frontend offers.</para>
/// </summary>
public sealed class TableDocxGenerator
{
    /// <summary>Landscape when the table is wide enough to need it.</summary>
    public MemoryStream Generate(
        string title,
        string subtitle,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool landscape = false)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            body.AppendChild(Heading(title, 32, bold: true));
            if (!string.IsNullOrWhiteSpace(subtitle))
                body.AppendChild(Heading(subtitle, 22, bold: false));
            body.AppendChild(Heading(string.Empty, 18, bold: false));

            body.AppendChild(BuildTable(headers, rows));

            body.AppendChild(Heading(
                $"พิมพ์เมื่อ {DateTime.UtcNow.AddHours(7):dd/MM/yyyy HH:mm} น.", 16, bold: false));

            body.AppendChild(SectionProperties(landscape));
        }

        ms.Position = 0;
        // Same absolute root-relationship defect as feedback id=41 — normalise
        // before the bytes leave the process, not at each call site.
        return (MemoryStream)DocxPackageNormalizer.Normalize(ms);
    }

    private static Paragraph Heading(string text, int halfPoints, bool bold)
    {
        var runProps = new RunProperties(
            new RunFonts { Ascii = "TH SarabunPSK", HighAnsi = "TH SarabunPSK", ComplexScript = "TH SarabunPSK" },
            new FontSize { Val = halfPoints.ToString() },
            new FontSizeComplexScript { Val = halfPoints.ToString() });
        if (bold)
        {
            runProps.AppendChild(new Bold());
            runProps.AppendChild(new BoldComplexScript());
        }

        return new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { After = "60", Before = "0" }),
            new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Table BuildTable(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = new Table(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }),
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }));

        var headerRow = new TableRow(new TableRowProperties(new TableHeader()));
        foreach (var h in headers) headerRow.AppendChild(Cell(h, bold: true, shaded: true));
        table.AppendChild(headerRow);

        foreach (var r in rows)
        {
            var tr = new TableRow();
            for (var i = 0; i < headers.Count; i++)
                tr.AppendChild(Cell(i < r.Count ? r[i] : string.Empty, bold: false, shaded: false));
            table.AppendChild(tr);
        }

        return table;
    }

    private static TableCell Cell(string text, bool bold, bool shaded)
    {
        var runProps = new RunProperties(
            new RunFonts { Ascii = "TH SarabunPSK", HighAnsi = "TH SarabunPSK", ComplexScript = "TH SarabunPSK" },
            new FontSize { Val = "28" },
            new FontSizeComplexScript { Val = "28" });
        if (bold)
        {
            runProps.AppendChild(new Bold());
            runProps.AppendChild(new BoldComplexScript());
        }

        var cellProps = new TableCellProperties(
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
        if (shaded)
            cellProps.AppendChild(new Shading { Fill = "E0F2FE", Val = ShadingPatternValues.Clear });

        return new TableCell(
            cellProps,
            new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "20", Before = "20" }),
                new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
    }

    private static SectionProperties SectionProperties(bool landscape) =>
        new(
            landscape
                ? new PageSize { Width = 16838U, Height = 11906U, Orient = PageOrientationValues.Landscape }
                : new PageSize { Width = 11906U, Height = 16838U },
            new PageMargin { Top = 720, Right = 720, Bottom = 720, Left = 720 });
}
