using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Gateway.Controllers;

namespace Gateway.Services.Reporting;

/// <summary>
/// Plan #47 — emits a single-page A4-landscape DOCX representation of the
/// school's administrative structure. Uses paragraph + table layout (not an
/// image render) so the file is editable in Word.
///
/// Layout:
///   • Title (school name + fiscal year)
///   • Director paragraph
///   • Board members compact paragraph (sideways info)
///   • Deputies row (if any)
///   • 4-column table: division name + head + numbered tasks
///   • Footer
/// </summary>
public sealed class OrgStructureDocxGenerator
{
    // Pastel fills matching the web chart (sky / violet / emerald / amber).
    private static readonly string[] DivisionFills = { "BAE6FD", "DDD6FE", "A7F3D0", "FED7AA" };
    private static readonly string[] DivisionTitleColors = { "0369A1", "6D28D9", "047857", "B45309" };
    private const string DirectorFill = "FBCFE8"; // pink-200
    private const string BoardFill = "C7D2FE";    // indigo-200

    public MemoryStream Generate(OrgStructureDto data)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            // ── Section: A4 landscape, narrow margins ───────────────────────
            body.AppendChild(BuildSectionProperties());

            // ── Title ──────────────────────────────────────────────────────
            body.AppendChild(TitleParagraph("แผนภูมิโครงสร้างการบริหารงานโรงเรียน", 28, true));
            body.AppendChild(TitleParagraph($"โรงเรียน{data.SchoolName}", 24, true));
            body.AppendChild(TitleParagraph($"ปีการศึกษา {data.FiscalYear}", 14, false));
            body.AppendChild(Spacer(120));

            // ── Director + Board (one row, side by side) ───────────────────
            body.AppendChild(BuildLeadershipTable(data));
            body.AppendChild(Spacer(120));

            // ── Deputies (if any) ──────────────────────────────────────────
            if (data.Deputies().Count > 0)
            {
                body.AppendChild(TitleParagraph("รองผู้อำนวยการ", 12, true));
                foreach (var d in data.Deputies())
                {
                    body.AppendChild(TitleParagraph("• " + d.FullName, 12, false));
                }
                body.AppendChild(Spacer(120));
            }

            // ── 4-division table ───────────────────────────────────────────
            body.AppendChild(BuildDivisionsTable(data));

            // ── Footer ─────────────────────────────────────────────────────
            body.AppendChild(Spacer(160));
            body.AppendChild(TitleParagraph(
                "สำนักงานเขตพื้นที่การศึกษาประถมศึกษาศรีสะเกษ เขต 3 · SBD Platform",
                9, false, "94A3B8"));

            main.Document.Save();
        }
        ms.Position = 0;
        return ms;
    }

    // ─── Layout helpers ──────────────────────────────────────────────────────

    private static SectionProperties BuildSectionProperties() => new(
        new PageSize { Width = 16838u, Height = 11906u, Orient = PageOrientationValues.Landscape },
        // Narrow margins (~10mm = 567 twentieths-of-a-point)
        new PageMargin { Top = 567, Right = 567, Bottom = 567, Left = 567, Header = 0, Footer = 0 }
    );

    private static Paragraph TitleParagraph(string text, int halfPt, bool bold, string colorHex = "1E293B")
    {
        var run = new Run();
        var runProps = new RunProperties(
            new RunFonts { Ascii = "Tahoma", HighAnsi = "Tahoma", ComplexScript = "Tahoma" },
            new FontSize { Val = (halfPt * 2).ToString() },
            new FontSizeComplexScript { Val = (halfPt * 2).ToString() },
            new Color { Val = colorHex });
        if (bold)
        {
            runProps.AppendChild(new Bold());
            runProps.AppendChild(new BoldComplexScript());
        }
        run.AppendChild(runProps);
        run.AppendChild(new Text(text));

        var p = new Paragraph(new ParagraphProperties(
            new Justification { Val = JustificationValues.Center },
            new SpacingBetweenLines { After = "60", Before = "0" }));
        p.AppendChild(run);
        return p;
    }

    private static Paragraph Spacer(int twentiethsOfPt)
    {
        return new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { After = twentiethsOfPt.ToString(), Before = "0" }));
    }

    private static Table BuildLeadershipTable(OrgStructureDto data)
    {
        var director = data.Leadership.FirstOrDefault(l => l.IsDirector);
        var boardCount = data.Board.Count;

        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableBorders(
                new TopBorder { Val = BorderValues.None, Size = 0 },
                new BottomBorder { Val = BorderValues.None, Size = 0 },
                new LeftBorder { Val = BorderValues.None, Size = 0 },
                new RightBorder { Val = BorderValues.None, Size = 0 },
                new InsideHorizontalBorder { Val = BorderValues.None, Size = 0 },
                new InsideVerticalBorder { Val = BorderValues.None, Size = 0 }
            )));

        var row = new TableRow();
        // Director cell (60% width)
        row.AppendChild(BuildBoxCell(
            director?.FullName ?? "— ยังไม่ระบุ —",
            director != null ? "ผู้อำนวยการ" : null,
            DirectorFill, "BE185D", 6000));
        // Board cell (40%)
        row.AppendChild(BuildBoxCell(
            boardCount > 0 ? $"{boardCount} คน" : "— ยังไม่ระบุ —",
            "คณะกรรมการสถานศึกษา",
            BoardFill, "4338CA", 3000));
        table.AppendChild(row);
        return table;
    }

    private static TableCell BuildBoxCell(string main, string? subLabel, string fillHex, string subColorHex, int widthDxa)
    {
        var cell = new TableCell();
        cell.AppendChild(new TableCellProperties(
            new TableCellWidth { Width = widthDxa.ToString(), Type = TableWidthUnitValues.Dxa },
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fillHex },
            new TableCellMargin(
                new TopMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "120", Type = TableWidthUnitValues.Dxa })));
        if (!string.IsNullOrEmpty(subLabel))
        {
            cell.AppendChild(TitleParagraph(subLabel, 8, false, subColorHex));
        }
        cell.AppendChild(TitleParagraph(main, 13, true));
        return cell;
    }

    private static Table BuildDivisionsTable(OrgStructureDto data)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "F1F5F9" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" }
            )));

        // Column widths grid (4 equal)
        var grid = new TableGrid();
        for (var i = 0; i < 4; i++) grid.AppendChild(new GridColumn { Width = "3750" });
        table.AppendChild(grid);

        // Row 1: division name headers (colored)
        var headerRow = new TableRow();
        for (var i = 0; i < data.WorkGroups.Count && i < 4; i++)
        {
            var wg = data.WorkGroups[i];
            var cell = new TableCell();
            cell.AppendChild(new TableCellProperties(
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = DivisionFills[i] },
                new TableCellMargin(
                    new TopMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "80", Type = TableWidthUnitValues.Dxa })));
            cell.AppendChild(TitleParagraph(wg.Name, 11, true, DivisionTitleColors[i]));
            headerRow.AppendChild(cell);
        }
        table.AppendChild(headerRow);

        // Row 2: head names
        var headRow = new TableRow();
        for (var i = 0; i < data.WorkGroups.Count && i < 4; i++)
        {
            var wg = data.WorkGroups[i];
            var head = wg.Members.FirstOrDefault(m => m.Role == "หัวหน้าฝ่าย") ?? wg.Members.FirstOrDefault();
            var cell = new TableCell();
            cell.AppendChild(new TableCellProperties(
                new TableCellMargin(
                    new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa })));
            cell.AppendChild(TitleParagraph("หัวหน้าฝ่าย", 8, false, "94A3B8"));
            cell.AppendChild(TitleParagraph(head?.FullName ?? "— ยังไม่ระบุ —", 10, false, "1E293B"));
            headRow.AppendChild(cell);
        }
        table.AppendChild(headRow);

        // Row 3: task lists (one cell per division with numbered list)
        var tasksRow = new TableRow();
        for (var i = 0; i < data.WorkGroups.Count && i < 4; i++)
        {
            var wg = data.WorkGroups[i];
            var cell = new TableCell();
            cell.AppendChild(new TableCellProperties(
                new TableCellMargin(
                    new TopMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                    new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                    new RightMargin { Width = "120", Type = TableWidthUnitValues.Dxa })));
            if (wg.Tasks.Count == 0)
            {
                cell.AppendChild(TaskParagraph("— ไม่มีรายการงาน —", 0, "CBD5E1", italic: true));
            }
            else
            {
                for (var t = 0; t < wg.Tasks.Count; t++)
                {
                    cell.AppendChild(TaskParagraph(wg.Tasks[t].NameTh, t + 1, "334155"));
                }
            }
            tasksRow.AppendChild(cell);
        }
        table.AppendChild(tasksRow);

        return table;
    }

    private static Paragraph TaskParagraph(string text, int number, string colorHex, bool italic = false)
    {
        var prefix = number > 0 ? $"{number}. " : string.Empty;
        var run = new Run();
        var runProps = new RunProperties(
            new RunFonts { Ascii = "Tahoma", HighAnsi = "Tahoma", ComplexScript = "Tahoma" },
            new FontSize { Val = "16" },        // 8pt
            new FontSizeComplexScript { Val = "16" },
            new Color { Val = colorHex });
        if (italic)
        {
            runProps.AppendChild(new Italic());
            runProps.AppendChild(new ItalicComplexScript());
        }
        run.AppendChild(runProps);
        run.AppendChild(new Text(prefix + text) { Space = SpaceProcessingModeValues.Preserve });

        var p = new Paragraph(new ParagraphProperties(
            new Justification { Val = JustificationValues.Left },
            new SpacingBetweenLines { After = "30", Before = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }));
        p.AppendChild(run);
        return p;
    }
}

internal static class OrgStructureDtoExt
{
    public static List<LeadershipDto> Deputies(this OrgStructureDto d)
        => d.Leadership.Where(l => l.IsDeputy && !l.IsDirector).ToList();
}
