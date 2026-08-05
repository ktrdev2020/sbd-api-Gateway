using System.Text;
using Gateway.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers.Admin;

/// <summary>
/// Plan #103 — SuperAdmin feedback report: filterable list, grouped stats
/// (per module / page / role / category), triage status updates, and a
/// Markdown export designed to be pasted into an AI agent (Claude Code)
/// for analysis-and-fix sessions.
/// The FAB on/off toggle is NOT here — it's the `feedbackEnabled` key in
/// SystemSettingsController (Redis-backed system settings).
/// </summary>
[ApiController]
[Route("api/v1/admin/feedback")]
[Authorize(Roles = "super_admin,SuperAdmin")]
public class AdminFeedbackController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "new", "acknowledged", "resolved", "dismissed" };

    private static readonly Dictionary<string, string> CategoryLabels = new()
    {
        ["bug"] = "🐛 ข้อผิดพลาด",
        ["improvement"] = "✨ ควรปรับปรุง",
        ["feature"] = "➕ ขอเพิ่มฟีเจอร์",
        ["data"] = "📊 ข้อมูลไม่ถูกต้อง",
        ["other"] = "💬 อื่นๆ",
    };

    private readonly GatewayDbContext _db;
    private readonly ILogger<AdminFeedbackController> _logger;

    public AdminFeedbackController(SbdDbContext db, ILogger<AdminFeedbackController> logger)
    {
        _db = (GatewayDbContext)db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? moduleCode,
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = BuildFilteredQuery(moduleCode, role, status, category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(f =>
                EF.Functions.ILike(f.Message, term) ||
                EF.Functions.ILike(f.Url, term) ||
                (f.DisplayName != null && EF.Functions.ILike(f.DisplayName, term)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var q = _db.FeedbackEntries.AsNoTracking();
        var since7d = DateTimeOffset.UtcNow.AddDays(-7);

        var total = await q.CountAsync();
        var newCount = await q.CountAsync(f => f.Status == "new");
        var last7d = await q.CountAsync(f => f.CreatedAt >= since7d);

        var byModule = await q.GroupBy(f => f.ModuleCode ?? "(ไม่ระบุ)")
            .Select(g => new
            {
                moduleCode = g.Key,
                count = g.Count(),
                newCount = g.Count(f => f.Status == "new"),
            })
            .OrderByDescending(x => x.count).ToListAsync();

        var byCategory = await q.GroupBy(f => f.Category)
            .Select(g => new { category = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).ToListAsync();

        var byStatus = await q.GroupBy(f => f.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync();

        var byRole = await q.GroupBy(f => f.Role)
            .Select(g => new { role = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).ToListAsync();

        var topUrls = await q.GroupBy(f => f.Url)
            .Select(g => new { url = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).Take(10).ToListAsync();

        return Ok(new { total, newCount, last7d, byModule, byCategory, byStatus, byRole, topUrls });
    }

    public record UpdateStatusRequest(string Status, string? AdminNote);

    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateStatusRequest req)
    {
        if (!AllowedStatuses.Contains(req.Status ?? string.Empty))
            return BadRequest(new { message = "status ต้องเป็น new/acknowledged/resolved/dismissed" });

        // AsTracking is REQUIRED: the Gateway DbContext defaults to NoTracking
        // (Program.cs), so an untracked entity would be mutated in memory and
        // SaveChangesAsync would write nothing while still returning 200.
        var entry = await _db.FeedbackEntries.AsTracking().FirstOrDefaultAsync(f => f.Id == id);
        if (entry is null) return NotFound();

        entry.Status = req.Status!.ToLowerInvariant();
        if (req.AdminNote is not null) entry.AdminNote = req.AdminNote.Trim();
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Feedback #{Id} status → {Status} by {User}", id, entry.Status, User.Identity?.Name);
        return Ok(entry);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var entry = await _db.FeedbackEntries.FirstOrDefaultAsync(f => f.Id == id);
        if (entry is null) return NotFound();
        _db.FeedbackEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Markdown export grouped module → page → entries, ready to paste into an
    /// AI coding agent. Default scope = open items (new + acknowledged).
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? moduleCode,
        [FromQuery] string? role,
        [FromQuery] string? status = "new,acknowledged",
        [FromQuery] string? category = null)
    {
        var entries = await BuildFilteredQuery(moduleCode, role, status, category)
            .OrderBy(f => f.ModuleCode).ThenBy(f => f.Url).ThenByDescending(f => f.CreatedAt)
            .Take(1000)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("# SBD User Feedback Report");
        sb.AppendLine();
        sb.AppendLine($"- Exported: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"- Scope: status=[{status ?? "all"}] module=[{moduleCode ?? "all"}] role=[{role ?? "all"}] · {entries.Count} entries");
        sb.AppendLine();
        sb.AppendLine("> Each entry: what the user reported on which page, with their role tier.");
        sb.AppendLine("> Analyze per module, propose fixes, and reference the URL as the affected route.");

        foreach (var moduleGroup in entries.GroupBy(f => f.ModuleCode ?? "(ไม่ระบุโมดูล)"))
        {
            sb.AppendLine();
            sb.AppendLine($"## Module: {moduleGroup.Key} ({moduleGroup.Count()})");
            foreach (var urlGroup in moduleGroup.GroupBy(f => f.Url))
            {
                sb.AppendLine();
                sb.AppendLine($"### Page: `{urlGroup.Key}`");
                var pageTitle = urlGroup.Select(f => f.PageTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                if (pageTitle is not null) sb.AppendLine($"_{pageTitle}_");
                foreach (var f in urlGroup)
                {
                    var label = CategoryLabels.GetValueOrDefault(f.Category, f.Category);
                    sb.AppendLine();
                    sb.AppendLine($"- **[{label}]** ({f.Role}{(f.SchoolCode is not null ? $" · school {f.SchoolCode}" : "")}, {f.CreatedAt:yyyy-MM-dd}, status: {f.Status})");
                    foreach (var line in f.Message.Split('\n'))
                        sb.AppendLine($"  {line.TrimEnd()}");
                    if (!string.IsNullOrWhiteSpace(f.AdminNote))
                        sb.AppendLine($"  _Admin note: {f.AdminNote}_");
                }
            }
        }

        var fileName = $"sbd-feedback-{DateTimeOffset.UtcNow:yyyyMMdd-HHmm}.md";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/markdown; charset=utf-8", fileName);
    }

    private IQueryable<Models.FeedbackEntry> BuildFilteredQuery(
        string? moduleCode, string? role, string? status, string? category)
    {
        var query = _db.FeedbackEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(moduleCode))
            query = query.Where(f => f.ModuleCode == moduleCode);
        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(f => f.Role == role);
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(f => f.Category == category);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant()).ToArray();
            query = query.Where(f => statuses.Contains(f.Status));
        }
        return query;
    }
}
