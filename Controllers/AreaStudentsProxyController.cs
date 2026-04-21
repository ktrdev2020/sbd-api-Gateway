using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Thin proxy forwarding <c>/api/v1/area/{areaId}/students/*</c> calls to
/// StudentApi. Preserves the JWT and pass-through query string (including
/// schoolCodes supplied by Angular). Read-only GET endpoints only — mutations
/// for students live in SchoolAdminStudentsController (prompt 03).
/// </summary>
[ApiController]
[Route("api/v1/area/{areaId:long}/students")]
[Authorize]
public class AreaStudentsProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly SbdDbContext _db;
    private readonly ILogger<AreaStudentsProxyController> _logger;

    public AreaStudentsProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        SbdDbContext db,
        ILogger<AreaStudentsProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _logger = logger;
    }

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview([FromRoute] long areaId, CancellationToken ct)
    {
        var query = await EnsureSchoolCodesAsync(areaId, ct);
        return await ForwardGetAsync($"/api/v1/area/{areaId}/students/overview{query}", ct);
    }

    [HttpGet]
    public async Task<IActionResult> ListStudents([FromRoute] long areaId, CancellationToken ct)
    {
        var query = await EnsureSchoolCodesAsync(areaId, ct);
        return await ForwardGetAsync($"/api/v1/area/{areaId}/students{query}", ct);
    }

    /// <summary>
    /// If caller did not supply schoolCodes, auto-inject the set owned by this
    /// area (Gateway is the source of truth for area→schools). This keeps
    /// StudentApi free of cross-bounded-context queries.
    /// </summary>
    private async Task<string> EnsureSchoolCodesAsync(long areaId, CancellationToken ct)
    {
        var incoming = Request.Query;
        if (incoming.ContainsKey("schoolCodes"))
            return Request.QueryString.ToString();

        var codes = await _db.Schools.AsNoTracking()
            .Where(s => s.AreaId == areaId && s.IsActive && s.DeletedAt == null)
            .Select(s => s.SchoolCode)
            .ToListAsync(ct);

        var baseQuery = Request.QueryString.HasValue
            ? Request.QueryString.ToString() + "&"
            : "?";
        return $"{baseQuery}schoolCodes={Uri.EscapeDataString(string.Join(',', codes))}";
    }

    private async Task<IActionResult> ForwardGetAsync(string path, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var req = new HttpRequestMessage(HttpMethod.Get, StudentApiBase + path);

        // Preserve JWT so StudentApi's [Authorize] can validate
        if (Request.Headers.TryGetValue("Authorization", out var auth))
            req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());

        try
        {
            using var response = await http.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = body,
                ContentType = contentType,
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "StudentApi proxy failed for {Path}", path);
            return StatusCode(502, new { error = "StudentApi ไม่ตอบสนอง" });
        }
    }
}
