using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Thin proxy forwarding <c>/api/v1/school/{schoolCode}/students/*</c>
/// calls to StudentApi. Covers the SchoolAdmin CRUD surface plus the
/// 3 transfer operations (class/in/out) and the bulk-promote end-of-year
/// wizard endpoint. Preserves JWT + Content-Type on forwarded requests.
///
/// **DMC SmisCode bridge** (2026-04-28): incoming `schoolCode` is OBEC
/// 10-digit (Gateway PK), but StudentApi stores `school_code` as DMC SmisCode
/// 8-digit (legacy from DMC importer). This proxy translates OBEC →
/// SmisCode once via Gateway DB lookup, then forwards using SmisCode in the
/// URL path. Requests for schools without a SmisCode mapping (e.g. manually
/// created in Gateway, no DMC import) fall through with the original code —
/// StudentApi simply returns empty results, which is correct.
/// </summary>
[ApiController]
[Route("api/v1/school/{schoolCode}/students")]
[Authorize]
public class SchoolStudentsProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly SbdDbContext _db;
    private readonly ILogger<SchoolStudentsProxyController> _logger;

    public SchoolStudentsProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        SbdDbContext db,
        ILogger<SchoolStudentsProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Resolve the OBEC SchoolCode to its DMC SmisCode. Falls back to the
    /// original code if no mapping exists (e.g. manually-created schools).
    /// </summary>
    private async Task<string> ResolveSmisAsync(string schoolCode, CancellationToken ct)
    {
        var smis = await _db.Schools.AsNoTracking()
            .Where(s => s.SchoolCode == schoolCode)
            .Select(s => s.SmisCode)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(smis) ? schoolCode : smis;
    }

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    [HttpGet]
    public async Task<IActionResult> List([FromRoute] string schoolCode, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Get, $"/api/v1/school/{smis}/students{Request.QueryString}", ct);
    }

    /// <summary>
    /// Per-tier student count for a school × academic year (T16 of
    /// aplan-school-fiscal-flow). Forwarded to StudentApi where the
    /// aggregation lives. Plan #4 T5 added optional `?term=` for per-round dispatch.
    /// </summary>
    [HttpGet("count-by-tier")]
    public async Task<IActionResult> CountByTier([FromRoute] string schoolCode, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Get, $"/api/v1/school/{smis}/students/count-by-tier{Request.QueryString}", ct);
    }

    /// <summary>
    /// Plan #4 T5 — DMC-flagged poor student count per tier · drives BASIC_FUND_POOR
    /// budget calc (PER_INDIVIDUAL_DMC pattern). Requires explicit `?academicYear=&term=`.
    /// </summary>
    [HttpGet("poverty-count")]
    public async Task<IActionResult> PovertyCount([FromRoute] string schoolCode, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Get, $"/api/v1/school/{smis}/students/poverty-count{Request.QueryString}", ct);
    }

    /// <summary>
    /// Plan #8 T3 — DRAFT count-by-tier · returns DMC if available, else
    /// latest-enrollment + grade-bump fallback for "ร่างแผน" mode.
    /// </summary>
    [HttpGet("draft-count-by-tier")]
    public async Task<IActionResult> DraftCountByTier([FromRoute] string schoolCode, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Get, $"/api/v1/school/{smis}/students/draft-count-by-tier{Request.QueryString}", ct);
    }

    /// <summary>
    /// Plan #4 T5 — กสศ. CCT-approved student count per tier · drives BASIC_FUND_POOR_SPECIAL
    /// budget calc (PER_INDIVIDUAL_CCT pattern). Requires explicit `?academicYear=&term=`.
    /// </summary>
    [HttpGet("cct-by-tier")]
    public async Task<IActionResult> CctByTier([FromRoute] string schoolCode, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Get, $"/api/v1/school/{smis}/students/cct-by-tier{Request.QueryString}", ct);
    }

    [HttpGet("{studentId:long}")]
    public async Task<IActionResult> Get([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Get, $"/api/v1/school/{smis}/students/{studentId}", ct);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromRoute] string schoolCode, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Post, $"/api/v1/school/{smis}/students", ct);
    }

    [HttpPatch("{studentId:long}")]
    public async Task<IActionResult> Update([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Patch, $"/api/v1/school/{smis}/students/{studentId}", ct);
    }

    [HttpDelete("{studentId:long}")]
    public async Task<IActionResult> Deactivate([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Delete, $"/api/v1/school/{smis}/students/{studentId}", ct);
    }

    [HttpPost("{studentId:long}/transfer-class")]
    public async Task<IActionResult> TransferClass([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Post, $"/api/v1/school/{smis}/students/{studentId}/transfer-class", ct);
    }

    [HttpPost("transfer-in")]
    public async Task<IActionResult> TransferIn([FromRoute] string schoolCode, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Post, $"/api/v1/school/{smis}/students/transfer-in", ct);
    }

    [HttpPost("{studentId:long}/transfer-out")]
    public async Task<IActionResult> TransferOut([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Post, $"/api/v1/school/{smis}/students/{studentId}/transfer-out", ct);
    }

    /// <summary>
    /// End-of-year wizard. Forwards the request body verbatim to StudentApi
    /// which publishes a <c>BulkPromoteStudentsCommand</c> to RabbitMQ.
    /// Frontend gets a 202 + JobId immediately and subscribes to SignalR
    /// for progress events on group <c>school:&lt;code&gt;</c>.
    /// </summary>
    [HttpPost("bulk-promote")]
    public async Task<IActionResult> BulkPromote([FromRoute] string schoolCode, CancellationToken ct)
    {
        var smis = await ResolveSmisAsync(schoolCode, ct);
        return await ForwardAsync(HttpMethod.Post, $"/api/v1/school/{smis}/students/bulk-promote", ct);
    }

    private async Task<IActionResult> ForwardAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var req = new HttpRequestMessage(method, StudentApiBase + path);

        if (Request.Headers.TryGetValue("Authorization", out var auth))
            req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());

        if (method != HttpMethod.Get && method != HttpMethod.Delete && Request.ContentLength > 0)
        {
            // Buffer body — safe for small JSON payloads (CRUD, not file upload)
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            req.Content = new StringContent(
                body,
                System.Text.Encoding.UTF8,
                Request.ContentType ?? "application/json");
        }

        try
        {
            using var response = await http.SendAsync(req, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = responseBody,
                ContentType = contentType,
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "StudentApi proxy failed for {Method} {Path}", method, path);
            return StatusCode(502, new { error = "StudentApi ไม่ตอบสนอง" });
        }
    }
}
