using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

/// <summary>
/// Thin proxy forwarding <c>/api/v1/aplan/*</c> calls to BudgetApi.
/// Frontend <c>AplanApiService</c> (feature-aplan) targets this base path;
/// BudgetApi enforces JWT scope filtering on its own side (defense-in-depth).
/// Preserves JWT + Content-Type on forwarded requests.
/// </summary>
[ApiController]
[Route("api/v1/aplan")]
[Authorize]
public class AplanProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AplanProxyController> _logger;

    public AplanProxyController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<AplanProxyController> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string BudgetApiBase =>
        _config["ServiceUrls:BudgetApi"]
        ?? Environment.GetEnvironmentVariable("BUDGET_API_URL")
        ?? "http://localhost:5040";

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => ForwardAsync(HttpMethod.Get, $"/api/v1/aplan{Request.QueryString}", ct);

    [HttpGet("stats")]
    public Task<IActionResult> Stats(CancellationToken ct)
        => ForwardAsync(HttpMethod.Get, $"/api/v1/aplan/stats{Request.QueryString}", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get([FromRoute] string id, CancellationToken ct)
        => ForwardAsync(HttpMethod.Get, $"/api/v1/aplan/{id}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => ForwardAsync(HttpMethod.Post, "/api/v1/aplan", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update([FromRoute] string id, CancellationToken ct)
        => ForwardAsync(HttpMethod.Put, $"/api/v1/aplan/{id}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete([FromRoute] string id, CancellationToken ct)
        => ForwardAsync(HttpMethod.Delete, $"/api/v1/aplan/{id}", ct);

    private async Task<IActionResult> ForwardAsync(HttpMethod method, string path, CancellationToken ct)
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
            _logger.LogError(ex, "BudgetApi proxy failed for {Method} {Path}", method, path);
            return StatusCode(502, new { error = "BudgetApi ไม่ตอบสนอง" });
        }
    }
}
