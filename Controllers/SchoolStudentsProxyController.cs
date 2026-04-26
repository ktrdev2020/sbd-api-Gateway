using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

/// <summary>
/// Thin proxy forwarding <c>/api/v1/school/{schoolCode}/students/*</c>
/// calls to StudentApi. Covers the SchoolAdmin CRUD surface plus the
/// 3 transfer operations (class/in/out). Preserves JWT + Content-Type on
/// forwarded requests.
///
/// Bulk-promote remains out of scope — requires a WorkerService orchestrator
/// with SignalR progress phases, tracked separately in the plan.
/// </summary>
[ApiController]
[Route("api/v1/school/{schoolCode}/students")]
[Authorize]
public class SchoolStudentsProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SchoolStudentsProxyController> _logger;

    public SchoolStudentsProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<SchoolStudentsProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    [HttpGet]
    public Task<IActionResult> List([FromRoute] string schoolCode, CancellationToken ct)
        => ForwardAsync(HttpMethod.Get, $"/api/v1/school/{schoolCode}/students{Request.QueryString}", ct);

    /// <summary>
    /// Per-tier student count for a school × academic year (T16 of
    /// aplan-school-fiscal-flow). Forwarded to StudentApi where the
    /// aggregation lives.
    /// </summary>
    [HttpGet("count-by-tier")]
    public Task<IActionResult> CountByTier([FromRoute] string schoolCode, CancellationToken ct)
        => ForwardAsync(HttpMethod.Get, $"/api/v1/school/{schoolCode}/students/count-by-tier{Request.QueryString}", ct);

    [HttpGet("{studentId:long}")]
    public Task<IActionResult> Get([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
        => ForwardAsync(HttpMethod.Get, $"/api/v1/school/{schoolCode}/students/{studentId}", ct);

    [HttpPost]
    public Task<IActionResult> Create([FromRoute] string schoolCode, CancellationToken ct)
        => ForwardAsync(HttpMethod.Post, $"/api/v1/school/{schoolCode}/students", ct);

    [HttpPatch("{studentId:long}")]
    public Task<IActionResult> Update([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
        => ForwardAsync(HttpMethod.Patch, $"/api/v1/school/{schoolCode}/students/{studentId}", ct);

    [HttpDelete("{studentId:long}")]
    public Task<IActionResult> Deactivate([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
        => ForwardAsync(HttpMethod.Delete, $"/api/v1/school/{schoolCode}/students/{studentId}", ct);

    [HttpPost("{studentId:long}/transfer-class")]
    public Task<IActionResult> TransferClass([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
        => ForwardAsync(HttpMethod.Post, $"/api/v1/school/{schoolCode}/students/{studentId}/transfer-class", ct);

    [HttpPost("transfer-in")]
    public Task<IActionResult> TransferIn([FromRoute] string schoolCode, CancellationToken ct)
        => ForwardAsync(HttpMethod.Post, $"/api/v1/school/{schoolCode}/students/transfer-in", ct);

    [HttpPost("{studentId:long}/transfer-out")]
    public Task<IActionResult> TransferOut([FromRoute] string schoolCode, [FromRoute] long studentId, CancellationToken ct)
        => ForwardAsync(HttpMethod.Post, $"/api/v1/school/{schoolCode}/students/{studentId}/transfer-out", ct);

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
