using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SBD.ServiceRegistry;
using StackExchange.Redis;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ApiStatusController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceRegistry _registry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiStatusController> _logger;

    private static readonly Dictionary<string, string> ServiceDescriptions = new()
    {
        ["Gateway"] = "API Gateway",
        ["AuthService"] = "Authentication Service",
        ["AcademicApi"] = "Academic API",
        ["RealtimeService"] = "SignalR Realtime Service",
        ["AiService"] = "AI Service (Gemini)",
        ["CurriculumApi"] = "Curriculum API",
        ["PersonnelApi"] = "Personnel API",
        ["PersonnelAdminApi"] = "Personnel Admin API",
        ["BudgetApi"] = "Budget API",
        ["GeneralAdminApi"] = "General Admin API",
        ["QueueApi"] = "Queue Management API",
        ["McpService"] = "MCP Context Server (AI tool orchestration)",
    };

    public ApiStatusController(
        IConnectionMultiplexer redis,
        IServiceRegistry registry,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ApiStatusController> logger)
    {
        _redis = redis;
        _registry = registry;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("health")]
    public async Task<ActionResult<ApiStatusResponse>> GetAllServicesHealth()
    {
        var registeredServices = await _registry.GetAllServicesAsync();
        var serviceGroups = await BuildServiceGroupsAsync(registeredServices);
        var infrastructureChecks = await CheckInfrastructureAsync();

        return Ok(new ApiStatusResponse
        {
            Timestamp = DateTime.UtcNow,
            Services = serviceGroups,
            Infrastructure = infrastructureChecks
        });
    }

    [HttpGet("health/{serviceName}")]
    public async Task<ActionResult<ServiceGroupStatus>> GetServiceHealth(string serviceName)
    {
        var instances = await _registry.GetInstancesAsync(serviceName);

        if (instances.Count == 0)
        {
            // Fallback to static config
            var serviceUrls = _configuration.GetSection("ServiceUrls");
            var url = serviceUrls[serviceName];
            if (string.IsNullOrEmpty(url))
                return NotFound(new { message = $"Service '{serviceName}' not found" });

            var fallbackResult = await CheckInstanceHealthAsync(serviceName, "static-0", url, $"{url}/health");
            return Ok(new ServiceGroupStatus
            {
                ServiceName = serviceName,
                Description = ServiceDescriptions.GetValueOrDefault(serviceName, serviceName),
                TotalInstances = 1,
                HealthyInstances = fallbackResult.Status == "healthy" ? 1 : 0,
                Instances = [fallbackResult]
            });
        }

        var tasks = instances.Select(i =>
            CheckInstanceHealthAsync(i.ServiceName, i.InstanceId, i.BaseUrl, i.HealthUrl, i)).ToList();
        var results = await Task.WhenAll(tasks);

        return Ok(new ServiceGroupStatus
        {
            ServiceName = serviceName,
            Description = ServiceDescriptions.GetValueOrDefault(serviceName, serviceName),
            TotalInstances = results.Length,
            HealthyInstances = results.Count(r => r.Status == "healthy"),
            Instances = results.ToList()
        });
    }

    private async Task<List<ServiceGroupStatus>> BuildServiceGroupsAsync(
        IReadOnlyDictionary<string, IReadOnlyList<ServiceRegistryEntry>> registeredServices)
    {
        var groups = new List<ServiceGroupStatus>();
        var processedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Process all registered services (from Redis registry)
        var allTasks = new List<(string svcName, string desc, List<Task<InstanceHealthResult>> tasks)>();

        foreach (var (serviceName, instances) in registeredServices)
        {
            processedServices.Add(serviceName);
            var desc = ServiceDescriptions.GetValueOrDefault(serviceName, serviceName);
            var tasks = instances.Select(i =>
                CheckInstanceHealthAsync(i.ServiceName, i.InstanceId, i.BaseUrl, i.HealthUrl, i)).ToList();
            allTasks.Add((serviceName, desc, tasks));
        }

        // 2. Fallback: check static ServiceUrls for any unregistered services
        var serviceUrls = _configuration.GetSection("ServiceUrls");
        foreach (var kvp in ServiceDescriptions)
        {
            if (processedServices.Contains(kvp.Key)) continue;

            var url = serviceUrls[kvp.Key];
            if (string.IsNullOrEmpty(url)) continue;

            var tasks = new List<Task<InstanceHealthResult>>
            {
                CheckInstanceHealthAsync(kvp.Key, "static-0", url, $"{url}/health")
            };
            allTasks.Add((kvp.Key, kvp.Value, tasks));
        }

        // Await all
        foreach (var (svcName, desc, tasks) in allTasks)
        {
            var results = await Task.WhenAll(tasks);
            groups.Add(new ServiceGroupStatus
            {
                ServiceName = svcName,
                Description = desc,
                TotalInstances = results.Length,
                HealthyInstances = results.Count(r => r.Status == "healthy"),
                Instances = results.ToList()
            });
        }

        return groups.OrderBy(g => g.ServiceName).ToList();
    }

    private async Task<InstanceHealthResult> CheckInstanceHealthAsync(
        string serviceName, string instanceId, string baseUrl, string healthUrl,
        ServiceRegistryEntry? registryEntry = null)
    {
        var result = new InstanceHealthResult
        {
            InstanceId = instanceId,
            ServiceName = serviceName,
            BaseUrl = baseUrl,
            HealthUrl = healthUrl,
            CheckedAt = DateTime.UtcNow,
            StartedAt = registryEntry?.StartedAt,
            LastHeartbeat = registryEntry?.LastHeartbeat,
            Host = registryEntry?.Host ?? "unknown",
            Metadata = registryEntry?.Metadata ?? new()
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await client.GetAsync(healthUrl);
            sw.Stop();

            result.ResponseTimeMs = sw.ElapsedMilliseconds;
            result.StatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                result.Status = "healthy";
                result.Message = "Instance is running";
            }
            else
            {
                result.Status = "degraded";
                result.Message = $"HTTP {result.StatusCode}";
            }
        }
        catch (TaskCanceledException)
        {
            result.Status = "unhealthy";
            result.Message = "Request timed out (5s)";
            result.ResponseTimeMs = 5000;
        }
        catch (HttpRequestException ex)
        {
            result.Status = "unhealthy";
            result.Message = $"Connection failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            result.Status = "unhealthy";
            result.Message = $"Error: {ex.Message}";
            _logger.LogError(ex, "Health check failed for {Service}/{Instance}", serviceName, instanceId);
        }

        return result;
    }

    private async Task<InfrastructureStatus> CheckInfrastructureAsync()
    {
        var infra = new InfrastructureStatus();

        // Check Redis
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var db = _redis.GetDatabase();
            await db.PingAsync();
            sw.Stop();
            infra.Redis = new InfraComponentStatus
            {
                Status = "healthy",
                ResponseTimeMs = sw.ElapsedMilliseconds,
                Message = "Connected"
            };
        }
        catch (Exception ex)
        {
            infra.Redis = new InfraComponentStatus
            {
                Status = "unhealthy",
                Message = $"Connection failed: {ex.Message}"
            };
        }

        // Check RabbitMQ
        try
        {
            var rabbitUri = _configuration.GetConnectionString("RabbitMQ") ?? "amqp://guest:guest@localhost:5672";
            var uri = new Uri(rabbitUri);
            using var tcpClient = new System.Net.Sockets.TcpClient();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await tcpClient.ConnectAsync(uri.Host, uri.Port > 0 ? uri.Port : 5672);
            sw.Stop();
            infra.RabbitMQ = new InfraComponentStatus
            {
                Status = "healthy",
                ResponseTimeMs = sw.ElapsedMilliseconds,
                Message = "Connected"
            };
        }
        catch (Exception ex)
        {
            infra.RabbitMQ = new InfraComponentStatus
            {
                Status = "unhealthy",
                Message = $"Connection failed: {ex.Message}"
            };
        }

        // Check PostgreSQL
        try
        {
            var connStr = _configuration.GetConnectionString("PostgreSQL");
            if (!string.IsNullOrEmpty(connStr))
            {
                using var conn = new Npgsql.NpgsqlConnection(connStr);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync();
                sw.Stop();
                infra.PostgreSQL = new InfraComponentStatus
                {
                    Status = "healthy",
                    ResponseTimeMs = sw.ElapsedMilliseconds,
                    Message = "Connected"
                };
            }
        }
        catch (Exception ex)
        {
            infra.PostgreSQL = new InfraComponentStatus
            {
                Status = "unhealthy",
                Message = $"Connection failed: {ex.Message}"
            };
        }

        // Check MinIO
        try
        {
            var minioEndpoint = _configuration["MinIO:Endpoint"] ?? "localhost:9000";
            var useSSL = bool.Parse(_configuration["MinIO:UseSSL"] ?? "false");
            var scheme = useSSL ? "https" : "http";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await client.GetAsync($"{scheme}://{minioEndpoint}/minio/health/live");
            sw.Stop();
            infra.MinIO = new InfraComponentStatus
            {
                Status = response.IsSuccessStatusCode ? "healthy" : "degraded",
                ResponseTimeMs = sw.ElapsedMilliseconds,
                Message = response.IsSuccessStatusCode ? "Connected" : $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            infra.MinIO = new InfraComponentStatus
            {
                Status = "unhealthy",
                Message = $"Connection failed: {ex.Message}"
            };
        }

        return infra;
    }
}

// --- Response Models ---

public class ApiStatusResponse
{
    public DateTime Timestamp { get; set; }
    public List<ServiceGroupStatus> Services { get; set; } = [];
    public InfrastructureStatus Infrastructure { get; set; } = new();
}

public class ServiceGroupStatus
{
    public string ServiceName { get; set; } = "";
    public string Description { get; set; } = "";
    public int TotalInstances { get; set; }
    public int HealthyInstances { get; set; }
    public List<InstanceHealthResult> Instances { get; set; } = [];
}

public class InstanceHealthResult
{
    public string InstanceId { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string Host { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string HealthUrl { get; set; } = "";
    public string Status { get; set; } = "unknown";
    public string Message { get; set; } = "";
    public int? StatusCode { get; set; }
    public long? ResponseTimeMs { get; set; }
    public DateTime CheckedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class InfrastructureStatus
{
    public InfraComponentStatus Redis { get; set; } = new();
    public InfraComponentStatus RabbitMQ { get; set; } = new();
    public InfraComponentStatus PostgreSQL { get; set; } = new();
    public InfraComponentStatus MinIO { get; set; } = new();
}

public class InfraComponentStatus
{
    public string Status { get; set; } = "unknown";
    public long? ResponseTimeMs { get; set; }
    public string Message { get; set; } = "";
}
