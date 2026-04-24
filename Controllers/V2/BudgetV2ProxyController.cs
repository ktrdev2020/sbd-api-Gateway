using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers.V2;

/// <summary>
/// Thin catch-all proxy forwarding every <c>/api/v2/budget/*</c> call to BudgetApi.
/// JWT + Content-Type passthrough; BudgetApi enforces scope filtering
/// (defense-in-depth). Single catchall route keeps proxy surface maintenance-free
/// as T8 sessions add new endpoints — no Gateway change needed per session.
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

    [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH")]
    [Route("{**path}")]
    public Task<IActionResult> Passthrough([FromRoute] string path, CancellationToken ct)
    {
        var method = HttpMethod.Parse(Request.Method);
        var target = $"/api/v2/budget/{path}{Request.QueryString}";
        return Forward(method, target, ct);
    }

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
