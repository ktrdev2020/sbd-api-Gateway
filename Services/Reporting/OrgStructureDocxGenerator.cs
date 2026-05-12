using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Gateway.Controllers;

namespace Gateway.Services.Reporting;

/// <summary>
/// Plan #47 — emits a Word doc that mirrors the official obec org-chart
/// reference image (แผนภูมิที่ 10): title banner → director + board row →
/// deputies row → 4 division columns, each column showing the ฝ่าย header,
/// the head name, then ONE BOX PER TASK stacked vertically (numbered).
///
/// Implementation note: this is not SmartArt (the diagram-parts API in
/// OpenXML is prohibitively complex). The reference image's "boxes" are
/// just colored Word table cells with borders — that's exactly what we
/// emit here. Visual fidelity is high; the file is editable in Word.
/// </summary>
public sealed class OrgStructureDocxGenerator
{
    // ── Division palette: header dark + body cream/peach (matches reference) ─
    private static readonly DivisionStyle[] DivisionStyles = new[]
    {
        new DivisionStyle("BAE6FD", "0369A1", "FEF3C7"), // sky header · light cream tasks
        new DivisionStyle("DDD6FE", "6D28D9", "FCE7F3"), // violet header · pink tasks
        new DivisionStyle("A7F3D0", "047857", "FED7AA"), // emerald header · peach tasks
        new DivisionStyle("FED7AA", "B45309", "FEE2E2"), // amber header · light red tasks
    };
    private const string DirectorFill = "FBCFE8";
    private const string DirectorTitleColor = "BE185D";
    private const string BoardFill = "C7D2FE";
    private const string BoardTitleColor = "4338CA";
    private const string DeputyFill = "EDE9FE";
    private const string DeputyTitleColor = "6D28D9";
    private const string TitleBannerFill = "FEF9C3";   // yellow
    private const string TitleBannerBorder = "CA8A04"; // amber-600

    private sealed record DivisionStyle(string HeaderFill, string HeaderColor, string TaskFill);

    public MemoryStream Generate(OrgStructureDto data)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            body.AppendChild(BuildSectionProperties());

            // Title banner
            body.AppendChild(BuildTitleBanner(
                $"แผนภูมิโครงสร้างการบริหารงาน · โรงเรียน{data.SchoolName}"));
            body.AppendChild(Spacer(120));

            // Top row: director + board
            body.AppendChild(BuildTopRow(data));
            body.AppendChild(Spacer(80));

            // Deputies (if any)
            if (data.Deputies().Count > 0)
            {
                body.AppendChild(BuildDeputiesRow(data));
                body.AppendChild(Spacer(80));
            }

            // 4 division columns with full task list
            body.AppendChild(BuildDivisionsRow(data));

            main.Document.Save();
        }
        ms.Position = 0;
        return ms;
    }

    // ─── Section ────────────────────────────────────────────────────────────

    private static SectionProperties BuildSectionProperties() => new(
        new PageSize { Width = 16838u, Height = 11906u, Orient = PageOrientationValues.Landscape },
        new PageMargin { Top = 567, Right = 567, Bottom = 567, Left = 567, Header = 0, Footer = 0 }
    );

    // ─── Title banner (yellow box across the page) ──────────────────────────

    private static Table BuildTitleBanner(string text)
    {
        var t = new Table();
        t.AppendChild(SimpleTableProps(widthPct: "5000"));
        t.AppendChild(StandardBorders(TitleBannerBorder, BorderSize: 8));
        var row = new TableRow();
        var cell = new TableCell();
        cell.AppendChild(new TableCellProperties(
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = TitleBannerFill },
            CenterVertical(),
            CellMargin(80, 200, 80, 200)));
        cell.AppendChild(StyledParagraph(text, 16, bold: true, "1E293B", JustificationValues.Center));
        row.AppendChild(cell);
        t.AppendChild(row);
        return t;
    }

    // ─── Top row: Director (centered) + Board (right) ───────────────────────

    private static Table BuildTopRow(OrgStructureDto data)
    {
        var director = data.Leadership.FirstOrDefault(l => l.IsDirector);
        var boardCount = data.Board.Count;

        var t = new Table();
        t.AppendChild(SimpleTableProps(widthPct: "3500"));
        t.AppendChild(NoBorders());

        var grid = new TableGrid();
        grid.AppendChild(new GridColumn { Width = "5000" });
        grid.AppendChild(new GridColumn { Width = "3500" });
        t.AppendChild(grid);

        var row = new TableRow();

        // Director cell
        var dirCell = new TableCell();
        dirCell.AppendChild(new TableCellProperties(
            new TableCellWidth { Width = "5000", Type = TableWidthUnitValues.Dxa },
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = DirectorFill },
            new TableCellBorders(
                new TopBorder { Val = BorderValues.Single, Size = 8, Color = "F9A8D4" },
                new BottomBorder { Val = BorderValues.Single, Size = 8, Color = "F9A8D4" },
                new LeftBorder { Val = BorderValues.Single, Size = 8, Color = "F9A8D4" },
                new RightBorder { Val = BorderValues.Single, Size = 8, Color = "F9A8D4" }),
            CellMargin(120, 200, 120, 200)));
        dirCell.AppendChild(StyledParagraph("ผู้อำนวยการ", 11, bold: false, DirectorTitleColor, JustificationValues.Center));
        dirCell.AppendChild(StyledParagraph(director?.FullName ?? "— ยังไม่ระบุ —", 14, bold: true, "1E293B", JustificationValues.Center));
        row.AppendChild(dirCell);

        // Board cell (smaller, right)
        var boardCell = new TableCell();
        boardCell.AppendChild(new TableCellProperties(
            new TableCellWidth { Width = "3500", Type = TableWidthUnitValues.Dxa },
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = BoardFill },
            new TableCellBorders(
                new TopBorder { Val = BorderValues.Single, Size = 8, Color = "A5B4FC" },
                new BottomBorder { Val = BorderValues.Single, Size = 8, Color = "A5B4FC" },
                new LeftBorder { Val = BorderValues.Dashed, Size = 8, Color = "A5B4FC" },
                new RightBorder { Val = BorderValues.Single, Size = 8, Color = "A5B4FC" }),
            CellMargin(100, 120, 100, 120)));
        boardCell.AppendChild(StyledParagraph("คณะกรรมการสถานศึกษา", 10, bold: true, BoardTitleColor, JustificationValues.Center));
        boardCell.AppendChild(StyledParagraph(
            boardCount > 0 ? $"{boardCount} คน" : "ยังไม่ระบุ",
            11, bold: false, "1E293B", JustificationValues.Center));
        row.AppendChild(boardCell);

        t.AppendChild(row);
        return t;
    }

    // ─── Deputies row ───────────────────────────────────────────────────────

    private static Table BuildDeputiesRow(OrgStructureDto data)
    {
        var deputies = data.Deputies();
        var t = new Table();
        t.AppendChild(SimpleTableProps(widthPct: "3500"));
        t.AppendChild(NoBorders());

        var row = new TableRow();
        foreach (var d in deputies)
        {
            var cell = new TableCell();
            cell.AppendChild(new TableCellProperties(
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = DeputyFill },
                new TableCellBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 6, Color = "C4B5FD" },
                    new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "C4B5FD" },
                    new LeftBorder { Val = BorderValues.Single, Size = 6, Color = "C4B5FD" },
                    new RightBorder { Val = BorderValues.Single, Size = 6, Color = "C4B5FD" }),
                CellMargin(80, 160, 80, 160)));
            cell.AppendChild(StyledParagraph("รองผู้อำนวยการ", 9, bold: false, DeputyTitleColor, JustificationValues.Center));
            cell.AppendChild(StyledParagraph(d.FullName, 11, bold: true, "1E293B", JustificationValues.Center));
            row.AppendChild(cell);
        }
        t.AppendChild(row);
        return t;
    }

    // ─── 4-division row with task boxes ─────────────────────────────────────

    private static Table BuildDivisionsRow(OrgStructureDto data)
    {
        var t = new Table();
        t.AppendChild(SimpleTableProps(widthPct: "5000"));
        // Outer table: invisible borders, each cell is a column
        t.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None })));

        var grid = new TableGrid();
        for (var i = 0; i < 4; i++) grid.AppendChild(new GridColumn { Width = "3750" });
        t.AppendChild(grid);

        var row = new TableRow();
        for (var i = 0; i < data.WorkGroups.Count && i < 4; i++)
        {
            var wg = data.WorkGroups[i];
            var style = DivisionStyles[i];
            var cell = new TableCell();
            cell.AppendChild(new TableCellProperties(
                new TableCellWidth { Width = "3750", Type = TableWidthUnitValues.Dxa },
                CellMargin(60, 80, 60, 80)));
            // Nested table inside each division cell: header + head + N task rows
            cell.AppendChild(BuildDivisionInnerTable(wg, style));
            row.AppendChild(cell);
        }
        t.AppendChild(row);
        return t;
    }

    private static Table BuildDivisionInnerTable(OrgWorkGroupDto wg, DivisionStyle style)
    {
        var t = new Table();
        t.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "80", Type = TableWidthUnitValues.Dxa }),
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 6, Color = style.HeaderColor },
                new BottomBorder { Val = BorderValues.Single, Size = 6, Color = style.HeaderColor },
                new LeftBorder { Val = BorderValues.Single, Size = 6, Color = style.HeaderColor },
                new RightBorder { Val = BorderValues.Single, Size = 6, Color = style.HeaderColor },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = style.HeaderColor },
                new InsideVerticalBorder { Val = BorderValues.None })));

        // Header row (division name)
        var hRow = new TableRow();
        var hCell = new TableCell();
        hCell.AppendChild(new TableCellProperties(
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = style.HeaderFill },
            CellMargin(80, 100, 80, 100)));
        hCell.AppendChild(StyledParagraph(wg.Name, 11, bold: true, style.HeaderColor, JustificationValues.Center));
        hRow.AppendChild(hCell);
        t.AppendChild(hRow);

        // Head row
        var head = wg.Members.FirstOrDefault(m => m.Role == "หัวหน้าฝ่าย") ?? wg.Members.FirstOrDefault();
        var headRow = new TableRow();
        var headCell = new TableCell();
        headCell.AppendChild(new TableCellProperties(
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "FFFFFF" },
            CellMargin(60, 100, 60, 100)));
        if (head != null)
        {
            headCell.AppendChild(StyledParagraph("หัวหน้าฝ่าย", 8, bold: false, "94A3B8", JustificationValues.Center));
            headCell.AppendChild(StyledParagraph(head.FullName, 10, bold: false, "1E293B", JustificationValues.Center));
        }
        else
        {
            headCell.AppendChild(StyledParagraph("— ยังไม่ระบุหัวหน้าฝ่าย —", 9, bold: false, "94A3B8", JustificationValues.Center));
        }
        headRow.AppendChild(headCell);
        t.AppendChild(headRow);

        // Task rows — one cell per task (like the reference image's stacked boxes)
        if (wg.Tasks.Count == 0)
        {
            var emptyRow = new TableRow();
            var emptyCell = new TableCell();
            emptyCell.AppendChild(new TableCellProperties(
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F8FAFC" },
                CellMargin(80, 100, 80, 100)));
            emptyCell.AppendChild(StyledParagraph("ยังไม่มีรายการงาน", 9, bold: false, "CBD5E1", JustificationValues.Center));
            emptyRow.AppendChild(emptyCell);
            t.AppendChild(emptyRow);
        }
        else
        {
            for (var i = 0; i < wg.Tasks.Count; i++)
            {
                var task = wg.Tasks[i];
                var tRow = new TableRow();
                var tCell = new TableCell();
                tCell.AppendChild(new TableCellProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = style.TaskFill },
                    CellMargin(60, 100, 60, 100)));
                tCell.AppendChild(StyledParagraph(
                    $"{i + 1}. {task.NameTh}",
                    9, bold: false, "1E293B", JustificationValues.Left));
                tRow.AppendChild(tCell);
                t.AppendChild(tRow);
            }
        }

        return t;
    }

    // ─── Low-level helpers ──────────────────────────────────────────────────

    private static Paragraph StyledParagraph(string text, int halfPt, bool bold, string colorHex, JustificationValues align)
    {
        var run = new Run();
        var props = new RunProperties(
            new RunFonts { Ascii = "Tahoma", HighAnsi = "Tahoma", ComplexScript = "Tahoma" },
            new FontSize { Val = (halfPt * 2).ToString() },
            new FontSizeComplexScript { Val = (halfPt * 2).ToString() },
            new Color { Val = colorHex });
        if (bold)
        {
            props.AppendChild(new Bold());
            props.AppendChild(new BoldComplexScript());
        }
        run.AppendChild(props);
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        var p = new Paragraph(new ParagraphProperties(
            new Justification { Val = align },
            new SpacingBetweenLines { After = "20", Before = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }));
        p.AppendChild(run);
        return p;
    }

    private static Paragraph Spacer(int twentiethsOfPt) =>
        new(new ParagraphProperties(
            new SpacingBetweenLines { After = twentiethsOfPt.ToString(), Before = "0" }));

    private static TableProperties SimpleTableProps(string widthPct) =>
        new(
            new TableWidth { Width = widthPct, Type = TableWidthUnitValues.Pct },
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableLayout { Type = TableLayoutValues.Fixed });

    private static TableBorders NoBorders() => new(
        new TopBorder { Val = BorderValues.None },
        new BottomBorder { Val = BorderValues.None },
        new LeftBorder { Val = BorderValues.None },
        new RightBorder { Val = BorderValues.None },
        new InsideHorizontalBorder { Val = BorderValues.None },
        new InsideVerticalBorder { Val = BorderValues.None });

    private static TableBorders StandardBorders(string colorHex, uint BorderSize) => new(
        new TopBorder { Val = BorderValues.Single, Size = BorderSize, Color = colorHex },
        new BottomBorder { Val = BorderValues.Single, Size = BorderSize, Color = colorHex },
        new LeftBorder { Val = BorderValues.Single, Size = BorderSize, Color = colorHex },
        new RightBorder { Val = BorderValues.Single, Size = BorderSize, Color = colorHex });

    private static TableCellVerticalAlignment CenterVertical() =>
        new() { Val = TableVerticalAlignmentValues.Center };

    private static TableCellMargin CellMargin(int top, int right, int bottom, int left) => new(
        new TopMargin { Width = top.ToString(), Type = TableWidthUnitValues.Dxa },
        new RightMargin { Width = right.ToString(), Type = TableWidthUnitValues.Dxa },
        new BottomMargin { Width = bottom.ToString(), Type = TableWidthUnitValues.Dxa },
        new LeftMargin { Width = left.ToString(), Type = TableWidthUnitValues.Dxa });
}

internal static class OrgStructureDtoExt
{
    public static List<LeadershipDto> Deputies(this OrgStructureDto d)
        => d.Leadership.Where(l => l.IsDeputy && !l.IsDirector).ToList();
}
