using System.Text.Json;
using System.Text.Json.Nodes;
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
        var (status, body, contentType) = await ForwardRawAsync($"/api/v1/area/{areaId}/students/overview{query}", ct);
        if (status >= 200 && status < 300 && body.TrimStart().StartsWith('{'))
            body = await EnrichBySchoolNamesAsync(areaId, body, ct);
        return new ContentResult { StatusCode = status, Content = body, ContentType = contentType };
    }

    /// <summary>
    /// Enrich the StudentApi overview JSON's <c>bySchool</c> array with
    /// <c>schoolName</c> by looking up Gateway's Schools master via SmisCode.
    /// Bounded-context honors: only the proxy joins across domains.
    /// </summary>
    private async Task<string> EnrichBySchoolNamesAsync(long areaId, string body, CancellationToken ct)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var arr = node?["bySchool"]?.AsArray();
            if (arr is null || arr.Count == 0) return body;

            // Build SmisCode → SchoolName map for this area. AsNoTracking + ToDict.
            var nameBySmis = await _db.Schools.AsNoTracking()
                .Where(s => s.AreaId == areaId && s.SmisCode != null)
                .Select(s => new { s.SmisCode, s.SchoolCode, s.NameTh })
                .ToDictionaryAsync(s => s.SmisCode!, s => new { s.SchoolCode, s.NameTh }, ct);

            foreach (var item in arr)
            {
                var smis = item?["schoolCode"]?.GetValue<string>();
                if (smis is null) continue;
                if (nameBySmis.TryGetValue(smis, out var info))
                {
                    item!["schoolName"] = info.NameTh;
                    item!["obecSchoolCode"] = info.SchoolCode; // keep both codes for the frontend
                }
                else
                {
                    item!["schoolName"] = null;
                    item!["obecSchoolCode"] = null;
                }
            }

            return node!.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich bySchool with names — returning raw");
            return body;
        }
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
    ///
    /// **DMC SmisCode bridge** (2026-04-28): StudentDB stores `school_code`
    /// as DMC SmisCode (8-digit, e.g. `33030001`) because the DMC importer
    /// keyed on the SmisCode column from the source CSV. Gateway is canonical
    /// on OBEC SchoolCode (10-digit, e.g. `1033530251`). To match, this proxy
    /// injects the SmisCode set for the area instead of SchoolCode. Schools
    /// with null SmisCode are filtered (so manually-entered test schools
    /// without a DMC mapping still fall through cleanly).
    /// </summary>
    private async Task<string> EnsureSchoolCodesAsync(long areaId, CancellationToken ct)
    {
        var incoming = Request.Query;
        if (incoming.ContainsKey("schoolCodes"))
            return Request.QueryString.ToString();

        var codes = await _db.Schools.AsNoTracking()
            .Where(s => s.AreaId == areaId && s.IsActive && s.DeletedAt == null && s.SmisCode != null)
            .Select(s => s.SmisCode!)
            .ToListAsync(ct);

        var baseQuery = Request.QueryString.HasValue
            ? Request.QueryString.ToString() + "&"
            : "?";
        return $"{baseQuery}schoolCodes={Uri.EscapeDataString(string.Join(',', codes))}";
    }

    private async Task<IActionResult> ForwardGetAsync(string path, CancellationToken ct)
    {
        var (status, body, contentType) = await ForwardRawAsync(path, ct);
        return new ContentResult { StatusCode = status, Content = body, ContentType = contentType };
    }

    private async Task<(int status, string body, string contentType)> ForwardRawAsync(string path, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var req = new HttpRequestMessage(HttpMethod.Get, StudentApiBase + path);

        if (Request.Headers.TryGetValue("Authorization", out var auth))
            req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());

        try
        {
            using var response = await http.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return ((int)response.StatusCode, body, contentType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "StudentApi proxy failed for {Path}", path);
            return (502, "{\"error\":\"StudentApi ไม่ตอบสนอง\"}", "application/json");
        }
    }
}
