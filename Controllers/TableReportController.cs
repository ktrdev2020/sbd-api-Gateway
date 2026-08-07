using Gateway.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

/// <summary>
/// Feedback id=80 / id=83 — "เพิ่มเครื่องปริ้น pdf/doc" on the student roster and
/// the homeroom-advisor page.
///
/// <para>The obvious shape would be one docx endpoint per page, rendering from
/// the database. That would mean re-implementing, in C#, every column choice,
/// filter, sort order and label map the page already applies — grade-level
/// names, for one, live only in a frontend constant. The printout would then
/// drift from the screen, which is the opposite of what "print this page" means
/// to the person asking.</para>
///
/// <para>So the caller sends the table it is already displaying and gets it back
/// as Word. One endpoint serves every list page, the export always matches the
/// filters in effect, and no label map is duplicated.</para>
///
/// <para>PDF is handled by the browser's own print dialog rather than here:
/// LibreOffice lives only in the BudgetApi image and adding an office suite to
/// the API gateway to flatten a table is a bad trade.</para>
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize]
public class TableReportController : ControllerBase
{
    private const int MaxRows = 5000;
    private const int MaxColumns = 30;
    private const int MaxCellLength = 500;

    private readonly TableDocxGenerator _generator;
    private readonly TableXlsxGenerator _xlsx;

    public TableReportController(TableDocxGenerator generator, TableXlsxGenerator xlsx)
    {
        _generator = generator;
        _xlsx = xlsx;
    }

    public record TableReportRequest(
        string? Title,
        string? Subtitle,
        List<string> Headers,
        List<List<string>> Rows,
        bool Landscape = false);

    /// <summary>Feedback id=95 — same payload, Excel instead of Word.</summary>
    [HttpPost("table.xlsx")]
    public IActionResult TableXlsx([FromBody] TableReportRequest req)
    {
        var bad = Validate(req);
        if (bad is not null) return bad;

        var (title, subtitle, headers, rows) = Normalise(req);
        var stream = _xlsx.Generate(title, subtitle, headers, rows);
        return File(stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"report-{DateTime.UtcNow.AddHours(7):yyyyMMdd-HHmm}.xlsx");
    }

    [HttpPost("table.docx")]
    public IActionResult Table([FromBody] TableReportRequest req)
    {
        var bad = Validate(req);
        if (bad is not null) return bad;

        var (title, subtitle, headers, rows) = Normalise(req);
        var stream = _generator.Generate(title, subtitle, headers, rows, req.Landscape);
        return File(stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"report-{DateTime.UtcNow.AddHours(7):yyyyMMdd-HHmm}.docx");
    }

    private IActionResult? Validate(TableReportRequest req)
    {
        if (req.Headers is not { Count: > 0 })
            return BadRequest(new { error = "ต้องระบุหัวตารางอย่างน้อย 1 คอลัมน์" });
        if (req.Headers.Count > MaxColumns)
            return BadRequest(new { error = $"คอลัมน์เกิน {MaxColumns}" });
        if (req.Rows is { Count: > MaxRows })
            return BadRequest(new { error = $"ข้อมูลเกิน {MaxRows} แถว กรุณากรองให้แคบลงก่อนพิมพ์" });
        return null;
    }

    /// <summary>
    /// Truncates rather than rejects — a stray long cell should not cost the
    /// user their whole export.
    /// </summary>
    private static (string, string, IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string>>)
        Normalise(TableReportRequest req)
    {
        static string Clamp(string? s) =>
            string.IsNullOrEmpty(s) ? string.Empty
            : s.Length <= MaxCellLength ? s
            : s[..MaxCellLength];

        var headers = req.Headers.Select(Clamp).ToList();
        var rows = (req.Rows ?? new List<List<string>>())
            .Select(r => (IReadOnlyList<string>)r.Select(Clamp).ToList())
            .ToList();
        var title = Clamp(req.Title) is { Length: > 0 } t ? t : "รายงาน";

        return (title, Clamp(req.Subtitle), headers, rows);
    }
}
