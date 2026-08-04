using System.Security.Claims;
using Gateway.Data;
using Gateway.Models;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #103 — Feedback FAB submit surface for every authenticated role.
/// Client sends page context (url/module/pageTitle/viewport); identity and
/// scope (user id, role, school_code, area_id) are taken from JWT claims —
/// never from the request body — so reports can't be spoofed per role.
/// Admin triage/report lives in Admin/AdminFeedbackController.
///
/// Plan #108 — Submit also accepts anonymous callers (role recorded as
/// "public") while the SuperAdmin-managed `publicFeedbackEnabled` setting is
/// on. Anonymous traffic is rate-limited per client IP; the limiter is
/// best-effort (fails open when Redis is down, per the resilience pattern).
/// </summary>
[ApiController]
[Route("api/v1/feedback")]
[Authorize]
public class FeedbackController : ControllerBase
{
    // "account" = ขอความช่วยเหลือบัญชี (Plan #109) — the login page's
    // contact-admin form. Anonymous by nature, so it bypasses the
    // publicFeedbackEnabled event-window gate (but not the rate limiter).
    private static readonly HashSet<string> AllowedCategories =
        new(StringComparer.OrdinalIgnoreCase) { "bug", "improvement", "feature", "data", "other", "account" };

    private const string SettingsCacheKey = "system:settings:v1";
    private const int AnonymousWindowMinutes = 10;
    private const int AnonymousMaxPerWindow = 5;

    private readonly GatewayDbContext _db;
    private readonly ICacheService _cache;
    private readonly ILogger<FeedbackController> _logger;

    public FeedbackController(SbdDbContext db, ICacheService cache, ILogger<FeedbackController> logger)
    {
        _db = (GatewayDbContext)db;
        _cache = cache;
        _logger = logger;
    }

    public record SubmitFeedbackRequest(
        string Url,
        string Category,
        string Message,
        string? ModuleCode,
        string? PageTitle,
        string? Viewport,
        string? DisplayName);

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Submit([FromBody] SubmitFeedbackRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Url) || req.Url.Length > 500)
            return BadRequest(new { message = "url ไม่ถูกต้อง" });
        if (!AllowedCategories.Contains(req.Category ?? string.Empty))
            return BadRequest(new { message = "category ต้องเป็น bug/improvement/feature/data/other/account" });
        var message = (req.Message ?? string.Empty).Trim();
        if (message.Length < 5)
            return BadRequest(new { message = "กรุณาระบุรายละเอียดอย่างน้อย 5 ตัวอักษร" });
        if (message.Length > 4000)
            return BadRequest(new { message = "รายละเอียดยาวเกิน 4000 ตัวอักษร" });

        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var isAccountHelp = string.Equals(req.Category, "account", StringComparison.OrdinalIgnoreCase);
        if (!isAuthenticated)
        {
            if (!isAccountHelp && !await IsPublicFeedbackEnabledAsync())
                return StatusCode(403, new { message = "ขณะนี้ยังไม่เปิดรับข้อเสนอแนะจากบุคคลทั่วไป" });
            if (!await TryConsumeAnonymousQuotaAsync())
                return StatusCode(429, new { message = "ส่งข้อเสนอแนะถี่เกินไป กรุณารอสักครู่แล้วลองใหม่" });
        }

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int? userId = int.TryParse(userIdStr, out var parsed) ? parsed : null;
        var role = isAuthenticated
            ? User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? "unknown"
            : "public";
        var schoolCode = User.FindFirst("school_code")?.Value ?? User.FindFirst("school_id")?.Value;
        var areaId = User.FindFirst("area_id")?.Value;

        var entry = new FeedbackEntry
        {
            UserId = userId,
            DisplayName = Truncate(req.DisplayName, 200) ?? User.Identity?.Name,
            Role = Truncate(role, 50)!,
            SchoolCode = Truncate(schoolCode, 10),
            AreaId = Truncate(areaId, 20),
            Url = Truncate(req.Url, 500)!,
            ModuleCode = Truncate(req.ModuleCode, 50),
            PageTitle = Truncate(req.PageTitle, 200),
            Category = req.Category!.ToLowerInvariant(),
            Message = message,
            UserAgent = Truncate(Request.Headers.UserAgent.ToString(), 400),
            Viewport = Truncate(req.Viewport, 50),
            Status = "new",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.FeedbackEntries.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Feedback #{Id} [{Category}] from {Role} on {Url}",
            entry.Id, entry.Category, entry.Role, entry.Url);

        return Ok(new { id = entry.Id, createdAt = entry.CreatedAt });
    }

    /// <summary>Caller's recent submissions — lets the FAB show "ส่งไปแล้ว" history.</summary>
    [HttpGet("my")]
    public async Task<IActionResult> My()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(userIdStr, out var userId)) return Ok(Array.Empty<object>());

        var items = await _db.FeedbackEntries.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(20)
            .Select(f => new { f.Id, f.Url, f.ModuleCode, f.Category, f.Message, f.Status, f.CreatedAt })
            .ToListAsync();
        return Ok(items);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length <= max ? v : v[..max];
    }

    /// <summary>
    /// Reads the Plan #108 toggle from the same Redis hash SystemSettingsController
    /// owns. Missing key or Redis outage both yield false — the public gate must
    /// fail closed (it is opened deliberately around events, never by accident).
    /// </summary>
    private async Task<bool> IsPublicFeedbackEnabledAsync()
    {
        var stored = await _cache.GetAsync<Dictionary<string, object>>(SettingsCacheKey);
        if (stored is null || !stored.TryGetValue("publicFeedbackEnabled", out var raw) || raw is null)
            return false;
        return raw switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            System.Text.Json.JsonElement je when je.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => je.GetBoolean(),
            _ => false,
        };
    }

    /// <summary>
    /// Fixed-window per-IP limiter for anonymous submissions. Redis outage makes
    /// GetAsync return default and SetAsync a no-op, so the limiter fails open —
    /// a spam window during an outage beats blocking legitimate reports.
    /// </summary>
    private async Task<bool> TryConsumeAnonymousQuotaAsync()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].ToString();
        var ip = !string.IsNullOrWhiteSpace(forwarded)
            ? forwarded.Split(',')[0].Trim()
            : HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (AnonymousWindowMinutes * 60);
        var key = $"feedback:rl:{ip}:{bucket}";

        var count = await _cache.GetAsync<int>(key);
        if (count >= AnonymousMaxPerWindow)
        {
            _logger.LogWarning("Anonymous feedback rate-limited for {Ip}", ip);
            return false;
        }
        await _cache.SetAsync(key, count + 1, TimeSpan.FromMinutes(AnonymousWindowMinutes));
        return true;
    }
}
