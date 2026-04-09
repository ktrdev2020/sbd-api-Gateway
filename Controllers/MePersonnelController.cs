using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Single entry point for the user's own personnel record. Reads
/// <c>User.PersonnelContext</c> (set by AuthService PersonnelLinkedToUser
/// Consumer) and dispatches to the right downstream service:
///
///   "school"  → PersonnelApi (5030) — ครู/ผู้บริหารในโรงเรียน
///   "area"    → PersonnelAdminApi (5031) — บุคลากรสำนักงานเขต
///   "student" → StudentApi (5032 — Phase D, not yet implemented)
///   null      → 204 No Content (user has no personnel record)
///
/// The frontend (/settings/personnel) only needs to know about this endpoint.
/// Phase A.2.5 — see docs/architecture/SBD-AUTHORITY-SYSTEM.md
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MePersonnelController : ControllerBase
{
    private readonly SbdDbContext _context;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MePersonnelController> _logger;

    public MePersonnelController(
        SbdDbContext context,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<MePersonnelController> logger)
    {
        _context = context;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string PersonnelApiBase =>
        _config["ServiceUrls:PersonnelApi"]
        ?? Environment.GetEnvironmentVariable("PERSONNEL_API_URL")
        ?? "http://localhost:5030";

    private string PersonnelAdminApiBase =>
        _config["ServiceUrls:PersonnelAdminApi"]
        ?? Environment.GetEnvironmentVariable("PERSONNEL_ADMIN_API_URL")
        ?? "http://localhost:5031";

    private int? CurrentUserId
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("sub")?.Value;
            return claim != null && int.TryParse(claim, out var id) ? id : null;
        }
    }

    /// <summary>
    /// GET /api/v1/me/personnel — load the current user's personnel record.
    /// Returns 204 No Content if the user is not linked to any personnel record.
    /// </summary>
    [HttpGet("personnel")]
    public async Task<IActionResult> GetPersonnel(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId == null) return Unauthorized();

        var (context, _) = await ResolveContextAsync(userId.Value, ct);
        return await ForwardByContextAsync(
            context,
            schoolPath: $"/api/v1/personnel/by-user/{userId}",
            areaPath: $"/api/v1/area-personnel/by-user/{userId}",
            method: HttpMethod.Get,
            body: null,
            ct);
    }

    /// <summary>
    /// PATCH /api/v1/me/personnel — restricted self-edit. Server-side policy
    /// enforcement happens in the downstream service (PersonnelApi /
    /// PersonnelAdminApi PATCH /me/self).
    /// </summary>
    [HttpPatch("personnel")]
    public async Task<IActionResult> PatchPersonnel(
        [FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId == null) return Unauthorized();

        var (context, _) = await ResolveContextAsync(userId.Value, ct);
        return await ForwardByContextAsync(
            context,
            schoolPath: "/api/v1/personnel/me/self",
            areaPath: "/api/v1/area-personnel/me/self",
            method: HttpMethod.Patch,
            body: body.GetRawText(),
            ct);
    }

    /// <summary>GET /api/v1/me/personnel/educations — list user's educations</summary>
    [HttpGet("personnel/educations")]
    public async Task<IActionResult> ListEducations(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId == null) return Unauthorized();

        var (context, _) = await ResolveContextAsync(userId.Value, ct);
        return await ForwardByContextAsync(
            context,
            schoolPath: "/api/v1/personnel/me/educations",
            areaPath: "/api/v1/area-personnel/me/educations",
            method: HttpMethod.Get,
            body: null,
            ct);
    }

    [HttpPost("personnel/educations")]
    public async Task<IActionResult> CreateEducation(
        [FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId == null) return Unauthorized();

        var (context, _) = await ResolveContextAsync(userId.Value, ct);
        return await ForwardByContextAsync(
            context,
            schoolPath: "/api/v1/personnel/me/educations",
            areaPath: "/api/v1/area-personnel/me/educations",
            method: HttpMethod.Post,
            body: body.GetRawText(),
            ct);
    }

    [HttpPut("personnel/educations/{eduId:int}")]
    public async Task<IActionResult> UpdateEducation(
        int eduId, [FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId == null) return Unauthorized();

        var (context, _) = await ResolveContextAsync(userId.Value, ct);
        return await ForwardByContextAsync(
            context,
            schoolPath: $"/api/v1/personnel/me/educations/{eduId}",
            areaPath: $"/api/v1/area-personnel/me/educations/{eduId}",
            method: HttpMethod.Put,
            body: body.GetRawText(),
            ct);
    }

    [HttpDelete("personnel/educations/{eduId:int}")]
    public async Task<IActionResult> DeleteEducation(int eduId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId == null) return Unauthorized();

        var (context, _) = await ResolveContextAsync(userId.Value, ct);
        return await ForwardByContextAsync(
            context,
            schoolPath: $"/api/v1/personnel/me/educations/{eduId}",
            areaPath: $"/api/v1/area-personnel/me/educations/{eduId}",
            method: HttpMethod.Delete,
            body: null,
            ct);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve the user's PersonnelContext from the Users table. Returns
    /// (null, null) if the user has no personnel record yet.
    /// </summary>
    private async Task<(string? context, int? refId)> ResolveContextAsync(int userId, CancellationToken ct)
    {
        var row = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.PersonnelContext, u.PersonnelRefId })
            .FirstOrDefaultAsync(ct);
        return (row?.PersonnelContext, row?.PersonnelRefId);
    }

    private async Task<IActionResult> ForwardByContextAsync(
        string? context,
        string schoolPath,
        string areaPath,
        HttpMethod method,
        string? body,
        CancellationToken ct)
    {
        if (context == null) return NoContent();

        string baseUrl;
        string path;
        switch (context)
        {
            case "school":
                baseUrl = PersonnelApiBase;
                path = schoolPath;
                break;
            case "area":
                baseUrl = PersonnelAdminApiBase;
                path = areaPath;
                break;
            case "student":
                return StatusCode(501, new { message = "Student personnel endpoint not yet implemented (Phase D)" });
            default:
                return BadRequest(new { message = $"Unknown PersonnelContext '{context}'" });
        }

        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var req = new HttpRequestMessage(method, $"{baseUrl}{path}");
        if (body != null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        ForwardAuth(req);

        try
        {
            var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return await ForwardResponse(resp, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MePersonnel] Forward {Method} {Path} to {BaseUrl} failed", method, path, baseUrl);
            return StatusCode(503, new { message = $"Personnel service unavailable ({context})" });
        }
    }

    private void ForwardAuth(HttpRequestMessage req)
    {
        if (Request.Headers.TryGetValue("Authorization", out var auth) && auth.Count > 0)
        {
            var raw = auth[0]!;
            if (AuthenticationHeaderValue.TryParse(raw, out var parsed))
                req.Headers.Authorization = parsed;
        }
    }

    private static async Task<IActionResult> ForwardResponse(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/json";
        return new ContentResult
        {
            StatusCode = (int)resp.StatusCode,
            Content = body,
            ContentType = contentType
        };
    }
}
