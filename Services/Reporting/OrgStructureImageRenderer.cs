using Gateway.Controllers;
using SkiaSharp;

namespace Gateway.Services.Reporting;

/// <summary>
/// Plan #47 follow-up — renders the school administrative-structure chart as
/// a PNG using SkiaSharp. The PNG is then embedded as a single image in the
/// DOCX, giving pixel-perfect fidelity to the obec template (แผนภูมิที่ 10).
///
/// Layout (1700 × 1100 px, A4 landscape ~150 DPI):
///   Title yellow banner top                      [Director] -- dotted -- [Board]
///                                                     |
///                                                  [Deputies]
///                                                     |
///                                     ┌──────────┬───┴───┬──────────┐
///                                  [วิชาการ] [งบประมาณ] [บุคคล] [ทั่วไป]
///                                  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐
///                                  │head  │ │head  │ │head  │ │head  │
///                                  │tasks │ │tasks │ │tasks │ │tasks │
///                                  │...   │ │...   │ │...   │ │...   │
/// </summary>
public sealed class OrgStructureImageRenderer
{
    // ── Canvas ──────────────────────────────────────────────────────────────
    private const int CanvasW = 1700;
    private const int CanvasH = 1100;

    // ── Top hierarchy ───────────────────────────────────────────────────────
    private const int TitleY = 30;
    private const int TitleH = 60;

    private const int DirectorY = 130;
    private const int DirectorW = 360;
    private const int DirectorH = 90;
    private const int DirectorCenterX = CanvasW / 2 - 110; // shift left so board fits right

    private const int BoardW = 220;
    private const int BoardH = 70;
    private const int BoardGap = 60;     // dotted line length

    private const int DeputyY = 260;
    private const int DeputyW = 280;
    private const int DeputyH = 60;

    // ── Trunk + 4 divisions ────────────────────────────────────────────────
    private const int TrunkY = 360;
    private const int DivisionY = 380;
    private const int DivisionW = 380;
    private const int DivisionHeaderH = 50;
    private const int HeadRowH = 60;
    private const int TaskRowH = 38;
    private const int TaskGap = 4;
    private const int DivisionGap = 25;
    private const int OuterMargin = 30;

    // ── Colors ──────────────────────────────────────────────────────────────
    private static readonly SKColor TitleBgFill = new(0xFE, 0xF9, 0xC3);
    private static readonly SKColor TitleBgBorder = new(0xCA, 0x8A, 0x04);
    private static readonly SKColor TextDark = new(0x1E, 0x29, 0x3B);
    private static readonly SKColor TextMuted = new(0x47, 0x55, 0x69);
    private static readonly SKColor TextHint = new(0x94, 0xA3, 0xB8);
    private static readonly SKColor LineGray = new(0x64, 0x74, 0x8B);

    private static readonly SKColor DirectorFill = new(0xFB, 0xCF, 0xE8);
    private static readonly SKColor DirectorBorder = new(0xDB, 0x27, 0x77);
    private static readonly SKColor DirectorAccent = new(0xBE, 0x18, 0x5D);

    private static readonly SKColor BoardFill = new(0xC7, 0xD2, 0xFE);
    private static readonly SKColor BoardBorder = new(0x63, 0x66, 0xF1);
    private static readonly SKColor BoardAccent = new(0x43, 0x38, 0xCA);

    private static readonly SKColor DeputyFill = new(0xED, 0xE9, 0xFE);
    private static readonly SKColor DeputyBorder = new(0xA7, 0x8B, 0xFA);
    private static readonly SKColor DeputyAccent = new(0x6D, 0x28, 0xD9);

    private sealed record DivisionPalette(
        SKColor HeaderFill, SKColor HeaderBorder, SKColor HeaderTextColor,
        SKColor TaskFill, SKColor TaskBorder);

    private static readonly DivisionPalette[] Palettes =
    {
        new(new SKColor(0xBA, 0xE6, 0xFD), new SKColor(0x0E, 0xA5, 0xE9), new SKColor(0x03, 0x69, 0xA1),
            new SKColor(0xFE, 0xF3, 0xC7), new SKColor(0xCA, 0x8A, 0x04)),
        new(new SKColor(0xDD, 0xD6, 0xFE), new SKColor(0x8B, 0x5C, 0xF6), new SKColor(0x6D, 0x28, 0xD9),
            new SKColor(0xFC, 0xE7, 0xF3), new SKColor(0xDB, 0x27, 0x77)),
        new(new SKColor(0xA7, 0xF3, 0xD0), new SKColor(0x10, 0xB9, 0x81), new SKColor(0x04, 0x78, 0x57),
            new SKColor(0xFE, 0xD7, 0xAA), new SKColor(0xEA, 0x58, 0x0C)),
        new(new SKColor(0xFE, 0xD7, 0xAA), new SKColor(0xF9, 0x73, 0x16), new SKColor(0xB4, 0x53, 0x09),
            new SKColor(0xFE, 0xE2, 0xE2), new SKColor(0xEF, 0x44, 0x44)),
    };

    // ── Fonts ───────────────────────────────────────────────────────────────
    private const float TitleSize = 28f;
    private const float RoleSize = 14f;
    private const float NameSize = 22f;
    private const float DivisionTitleSize = 18f;
    private const float HeadNameSize = 15f;
    private const float TaskTextSize = 13f;

    public byte[] RenderPng(OrgStructureDto data)
    {
        var info = new SKImageInfo(CanvasW, CanvasH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var typeface = ResolveThaiTypeface();

        // ── Title banner ────────────────────────────────────────────────────
        DrawTitleBanner(canvas, $"แผนภูมิโครงสร้างการบริหารงานโรงเรียน{data.SchoolName}", typeface);

        // ── Director + dotted to Board ──────────────────────────────────────
        var director = data.Leadership.FirstOrDefault(l => l.IsDirector);
        var dirRect = new SKRect(DirectorCenterX, DirectorY, DirectorCenterX + DirectorW, DirectorY + DirectorH);
        DrawRoleBox(canvas, dirRect, "ผู้อำนวยการ", director?.FullName ?? "— ยังไม่ระบุ —",
            DirectorFill, DirectorBorder, DirectorAccent, typeface, accentSize: RoleSize, mainSize: NameSize);

        if (data.Board.Count > 0)
        {
            var boardY = DirectorY + (DirectorH - BoardH) / 2;
            var boardX = (int)dirRect.Right + BoardGap;
            var boardRect = new SKRect(boardX, boardY, boardX + BoardW, boardY + BoardH);
            DrawDottedHLine(canvas, dirRect.Right, boardX, dirRect.MidY);
            DrawRoleBox(canvas, boardRect, "คณะกรรมการสถานศึกษา", $"{data.Board.Count} คน",
                BoardFill, BoardBorder, BoardAccent, typeface, accentSize: 12f, mainSize: 16f);
        }

        // ── Vertical line down to Deputies / Trunk ──────────────────────────
        var trunkCenterX = dirRect.MidX;
        var deputies = data.Leadership.Where(l => l.IsDeputy && !l.IsDirector).ToList();
        var afterDirY = (int)dirRect.Bottom;

        if (deputies.Count > 0)
        {
            DrawVLine(canvas, trunkCenterX, afterDirY, DeputyY);
            // Center deputies horizontally
            var depTotalW = deputies.Count * DeputyW + (deputies.Count - 1) * DivisionGap;
            var depStartX = trunkCenterX - depTotalW / 2f;
            for (var i = 0; i < deputies.Count; i++)
            {
                var x = depStartX + i * (DeputyW + DivisionGap);
                var rect = new SKRect(x, DeputyY, x + DeputyW, DeputyY + DeputyH);
                DrawRoleBox(canvas, rect, "รองผู้อำนวยการ", deputies[i].FullName,
                    DeputyFill, DeputyBorder, DeputyAccent, typeface, accentSize: 11f, mainSize: 15f);
            }
            DrawVLine(canvas, trunkCenterX, DeputyY + DeputyH, TrunkY);
        }
        else
        {
            DrawVLine(canvas, trunkCenterX, afterDirY, TrunkY);
        }

        // ── 4 Divisions: horizontal trunk + drops + columns ────────────────
        DrawDivisions(canvas, data, trunkCenterX, typeface);

        using var image = surface.Snapshot();
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 92);
        return pngData.ToArray();
    }

    // ─── Major sub-renderers ────────────────────────────────────────────────

    private static void DrawTitleBanner(SKCanvas canvas, string text, SKTypeface tf)
    {
        var rect = new SKRect(OuterMargin, TitleY, CanvasW - OuterMargin, TitleY + TitleH);
        using var fill = new SKPaint { Color = TitleBgFill, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var border = new SKPaint
        {
            Color = TitleBgBorder, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 2f
        };
        canvas.DrawRoundRect(rect, 6f, 6f, fill);
        canvas.DrawRoundRect(rect, 6f, 6f, border);

        using var paint = new SKPaint
        {
            Color = TextDark, IsAntialias = true,
            TextSize = TitleSize, Typeface = tf,
            TextAlign = SKTextAlign.Center, FakeBoldText = true,
        };
        canvas.DrawText(text, rect.MidX, rect.MidY + TitleSize / 3f, paint);
    }

    private static void DrawRoleBox(SKCanvas canvas, SKRect rect, string role, string name,
        SKColor fill, SKColor border, SKColor accent, SKTypeface tf,
        float accentSize, float mainSize)
    {
        using var fillPaint = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var borderPaint = new SKPaint
        {
            Color = border, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f
        };
        canvas.DrawRoundRect(rect, 10f, 10f, fillPaint);
        canvas.DrawRoundRect(rect, 10f, 10f, borderPaint);

        using var rolePaint = new SKPaint
        {
            Color = accent, IsAntialias = true, TextSize = accentSize,
            Typeface = tf, TextAlign = SKTextAlign.Center
        };
        using var namePaint = new SKPaint
        {
            Color = TextDark, IsAntialias = true, TextSize = mainSize,
            Typeface = tf, TextAlign = SKTextAlign.Center, FakeBoldText = true
        };

        var lineGap = 6f;
        var totalH = accentSize + lineGap + mainSize;
        var y = rect.MidY - totalH / 2f + accentSize;
        canvas.DrawText(role, rect.MidX, y, rolePaint);
        canvas.DrawText(name, rect.MidX, y + lineGap + mainSize, namePaint);
    }

    private static void DrawDivisions(SKCanvas canvas, OrgStructureDto data, float trunkCenterX, SKTypeface tf)
    {
        var workGroups = data.WorkGroups;
        var n = Math.Min(workGroups.Count, 4);
        if (n == 0) return;

        // Compute column X positions (4 columns centered)
        var totalW = n * DivisionW + (n - 1) * DivisionGap;
        var startX = (CanvasW - totalW) / 2f;

        // Trunk horizontal line connects first column center to last
        var firstColMidX = startX + DivisionW / 2f;
        var lastColMidX = startX + (n - 1) * (DivisionW + DivisionGap) + DivisionW / 2f;
        DrawHLine(canvas, firstColMidX, lastColMidX, TrunkY);

        // Vertical line from trunk up to wherever the deputy/director line ends already drawn
        // (caller ensures vertical line ends at TrunkY)

        for (var i = 0; i < n; i++)
        {
            var x = startX + i * (DivisionW + DivisionGap);
            var midX = x + DivisionW / 2f;
            // Drop from trunk to division header
            DrawVLine(canvas, midX, TrunkY, DivisionY);
            DrawDivisionColumn(canvas, x, workGroups[i], Palettes[i], tf);
        }
    }

    private static void DrawDivisionColumn(SKCanvas canvas, float x, OrgWorkGroupDto wg, DivisionPalette p, SKTypeface tf)
    {
        // Header
        var headerRect = new SKRect(x, DivisionY, x + DivisionW, DivisionY + DivisionHeaderH);
        using var hFill = new SKPaint { Color = p.HeaderFill, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var hBorder = new SKPaint
        {
            Color = p.HeaderBorder, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 2f
        };
        canvas.DrawRoundRect(headerRect, 6f, 6f, hFill);
        canvas.DrawRoundRect(headerRect, 6f, 6f, hBorder);

        using var titlePaint = new SKPaint
        {
            Color = p.HeaderTextColor, IsAntialias = true,
            TextSize = DivisionTitleSize, Typeface = tf,
            TextAlign = SKTextAlign.Center, FakeBoldText = true
        };
        canvas.DrawText(wg.Name, headerRect.MidX, headerRect.MidY + DivisionTitleSize / 3f, titlePaint);

        // Head name row
        var headRect = new SKRect(x, headerRect.Bottom + 2, x + DivisionW, headerRect.Bottom + 2 + HeadRowH);
        using var headFill = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var headBorder = new SKPaint
        {
            Color = p.HeaderBorder, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f
        };
        canvas.DrawRoundRect(headRect, 4f, 4f, headFill);
        canvas.DrawRoundRect(headRect, 4f, 4f, headBorder);

        var head = wg.Members.FirstOrDefault(m => m.Role == "หัวหน้าฝ่าย") ?? wg.Members.FirstOrDefault();
        using var labelPaint = new SKPaint
        {
            Color = TextHint, IsAntialias = true, TextSize = 11f,
            Typeface = tf, TextAlign = SKTextAlign.Center
        };
        using var hnPaint = new SKPaint
        {
            Color = TextDark, IsAntialias = true, TextSize = HeadNameSize,
            Typeface = tf, TextAlign = SKTextAlign.Center, FakeBoldText = head != null
        };
        canvas.DrawText("หัวหน้าฝ่าย", headRect.MidX, headRect.Top + 18, labelPaint);
        canvas.DrawText(head?.FullName ?? "— ยังไม่ระบุ —",
            headRect.MidX, headRect.Top + 18 + 22, hnPaint);

        // Task boxes — auto-shrink so column doesn't overflow canvas
        var availableH = CanvasH - 40 - (int)headRect.Bottom - 8;
        var taskCount = wg.Tasks.Count;
        var effectiveRowH = (float)TaskRowH;
        var effectiveTextSize = TaskTextSize;
        if (taskCount > 0)
        {
            var totalNeeded = taskCount * TaskRowH + (taskCount - 1) * TaskGap;
            if (totalNeeded > availableH)
            {
                var scale = Math.Max(0.55f, availableH / (float)totalNeeded);
                effectiveRowH = TaskRowH * scale;
                effectiveTextSize = Math.Max(8f, TaskTextSize * scale);
            }
        }

        var ty = headRect.Bottom + 8;
        if (taskCount == 0)
        {
            var emptyRect = new SKRect(x, ty, x + DivisionW, ty + TaskRowH);
            using var emptyPaint = new SKPaint { Color = new SKColor(0xF8, 0xFA, 0xFC), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var emptyBorder = new SKPaint { Color = TextHint, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
            canvas.DrawRoundRect(emptyRect, 4f, 4f, emptyPaint);
            canvas.DrawRoundRect(emptyRect, 4f, 4f, emptyBorder);
            using var ePaint = new SKPaint
            {
                Color = TextHint, IsAntialias = true, TextSize = 12f,
                Typeface = tf, TextAlign = SKTextAlign.Center
            };
            canvas.DrawText("— ไม่มีรายการงาน —", emptyRect.MidX, emptyRect.MidY + 4, ePaint);
        }
        else
        {
            using var tFill = new SKPaint { Color = p.TaskFill, IsAntialias = true, Style = SKPaintStyle.Fill };
            using var tBorder = new SKPaint
            {
                Color = p.TaskBorder, IsAntialias = true,
                Style = SKPaintStyle.Stroke, StrokeWidth = 1f
            };
            using var tText = new SKPaint
            {
                Color = TextDark, IsAntialias = true, TextSize = effectiveTextSize,
                Typeface = tf, TextAlign = SKTextAlign.Left
            };

            for (var i = 0; i < taskCount; i++)
            {
                var task = wg.Tasks[i];
                var rowRect = new SKRect(x, ty, x + DivisionW, ty + effectiveRowH);
                canvas.DrawRoundRect(rowRect, 4f, 4f, tFill);
                canvas.DrawRoundRect(rowRect, 4f, 4f, tBorder);

                var label = $"{i + 1}. {task.NameTh}";
                // Truncate text if too wide
                var maxW = DivisionW - 16;
                var measured = tText.MeasureText(label);
                if (measured > maxW)
                {
                    while (label.Length > 0 && tText.MeasureText(label + "...") > maxW)
                        label = label[..^1];
                    label += "...";
                }
                canvas.DrawText(label, rowRect.Left + 8, rowRect.MidY + effectiveTextSize / 3f, tText);

                ty += effectiveRowH + TaskGap;
            }
        }
    }

    // ─── Line/connector primitives ──────────────────────────────────────────

    private static void DrawVLine(SKCanvas canvas, float x, float y1, float y2)
    {
        using var paint = new SKPaint
        {
            Color = LineGray, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 2f
        };
        canvas.DrawLine(x, y1, x, y2, paint);
    }

    private static void DrawHLine(SKCanvas canvas, float x1, float x2, float y)
    {
        using var paint = new SKPaint
        {
            Color = LineGray, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 2f
        };
        canvas.DrawLine(x1, y, x2, y, paint);
    }

    private static void DrawDottedHLine(SKCanvas canvas, float x1, float x2, float y)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(0xA7, 0x8B, 0xFA), IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 2f,
            PathEffect = SKPathEffect.CreateDash(new float[] { 8f, 6f }, 0)
        };
        canvas.DrawLine(x1, y, x2, y, paint);
    }

    // ─── Thai-capable typeface resolution ──────────────────────────────────

    private static SKTypeface ResolveThaiTypeface()
    {
        // Match on a Thai code point; system fallback picks up Sarabun/Loma/etc.
        // (Container has fonts-thai-tlwg installed for LibreOffice → SkiaSharp finds them.)
        var tf = SKFontManager.Default.MatchCharacter('ก');
        return tf ?? SKTypeface.Default;
    }
}
