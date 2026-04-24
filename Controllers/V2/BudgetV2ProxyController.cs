using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers.V2;

/// <summary>
/// Thin proxy forwarding <c>/api/v2/budget/*</c> calls to BudgetApi.
/// JWT + Content-Type passthrough; BudgetApi enforces scope filtering
/// (defense-in-depth). Expanded per T8 session:
///   - session-1: lookups (6 read-only endpoints)
///   - session-2+: planning / activity / forms-docs-inbox
/// Pattern-copy of <see cref="AplanProxyController"/>.
/// </summary>
[ApiController]
[Route("api/v2/budget")]
[Authorize]
public class BudgetV2ProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<BudgetV2ProxyController> _logger;

    public BudgetV2ProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<BudgetV2ProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string BudgetApiBase =>
        _config["ServiceUrls:BudgetApi"]
        ?? Environment.GetEnvironmentVariable("BUDGET_API_URL")
        ?? "http://localhost:5040";

    // ---------- Lookups (T8 session-1) ----------

    [HttpGet("lookups/funding-tiers")]
    public Task<IActionResult> LookupFundingTiers(CancellationToken ct)
        => Forward(HttpMethod.Get, "/api/v2/budget/lookups/funding-tiers", ct);

    [HttpGet("lookups/fund-sources")]
    public Task<IActionResult> LookupFundSources(CancellationToken ct)
        => Forward(HttpMethod.Get, "/api/v2/budget/lookups/fund-sources", ct);

    [HttpGet("lookups/subsidy-categories")]
    public Task<IActionResult> LookupSubsidyCategories(CancellationToken ct)
        => Forward(HttpMethod.Get, "/api/v2/budget/lookups/subsidy-categories", ct);

    [HttpGet("lookups/budget-buckets")]
    public Task<IActionResult> LookupBudgetBuckets(CancellationToken ct)
        => Forward(HttpMethod.Get, "/api/v2/budget/lookups/budget-buckets", ct);

    [HttpGet("lookups/std-dimensions")]
    public Task<IActionResult> LookupStdDimensions(CancellationToken ct)
        => Forward(HttpMethod.Get, $"/api/v2/budget/lookups/std-dimensions{Request.QueryString}", ct);

    [HttpGet("lookups/standards")]
    public Task<IActionResult> LookupStandards(CancellationToken ct)
        => Forward(HttpMethod.Get, $"/api/v2/budget/lookups/standards{Request.QueryString}", ct);

    // ---------- Forward helper ----------

    private async Task<IActionResult> Forward(HttpMethod method, string path, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var req = new HttpRequestMessage(method, BudgetApiBase + path);

        if (Request.Headers.TryGetValue("Authorization", out var auth))
            req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());

        if (method != HttpMethod.Get && method != HttpMethod.Delete && Request.ContentLength > 0)
        {
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
            _logger.LogError(ex, "BudgetApi v2 proxy failed for {Method} {Path}", method, path);
            return StatusCode(502, new { error = "BudgetApi ไม่ตอบสนอง" });
        }
    }
}
