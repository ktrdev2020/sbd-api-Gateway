using Gateway.Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using StackExchange.Redis;

namespace Gateway.Controllers;

/// <summary>
/// Redis Cache Manager API — multi-DB inspection, key management,
/// and DB-backed CacheDefinition registry for the SBD platform.
///
/// DB layout:
///   DB 0 — HTTP Cache &amp; Service Registry (Gateway + all domain APIs)
///   DB 1 — Auth Sessions (AuthService: refresh_token, session_meta, …)
///   DB 2 — Reserved (ยังไม่ใช้งาน)
/// </summary>
[ApiController]
[Route("api/v1/admin/cache")]
[Authorize]
public class CacheController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServer _server;
    private readonly GatewayDbContext _db;
    private readonly ILogger<CacheController> _logger;

    // GatewayDbContext is registered as SbdDbContext in DI — cast on constructor

    // ── Static DB configuration ──────────────────────────────────────────────
    private static readonly RedisDbConfig[] DbConfigs =
    [
        new(0, "HTTP Cache & บริการ", "API response cache, service registry, rate limiting",
        [
            new("refdata:",        "Reference Data",          "ข้อมูลอ้างอิง (จังหวัด อำเภอ บทบาท โมดูล)", "24 ชั่วโมง"),
            new("svc:",            "Service Registry",        "รายการ microservice ที่ลงทะเบียน",           "ตลอดเวลา"),
            new("rate:",           "Rate Limiting",           "การจำกัด API call ต่อ IP/user",              "1 นาที"),
            new("cache:",          "General API Cache",       "Response cache จาก ICacheService",           "5-60 นาที"),
        ]),
        new(1, "Auth Sessions", "Refresh token, session metadata ของผู้ใช้ (AuthService)",
        [
            new("refresh_token:", "Refresh Token Hash",       "SHA-256 hash ของ refresh token แต่ละ session", "90 วัน"),
            new("user_sessions:", "User Sessions Set",        "SET ของ sessionId ทั้งหมดของแต่ละ user",       "90 วัน"),
            new("session_meta:",  "Session Metadata",         "device, IP, UA, LastSeenAt ของ session",       "90 วัน"),
            new("session_token:", "Session Token Reference",  "อ้างอิง hash ปัจจุบัน (ใช้ตอน rotate)",       "90 วัน"),
        ]),
        new(2, "Reserved", "สำรองสำหรับการใช้งานในอนาคต", []),
    ];

    public CacheController(
        IConnectionMultiplexer redis,
        SbdDbContext db,               // registered as SbdDbContext; GatewayDbContext at runtime
        ILogger<CacheController> logger)
    {
        _redis = redis;
        _server = redis.GetServer(redis.GetEndPoints().First());
        _db = (GatewayDbContext)db;    // safe — DI resolves GatewayDbContext for SbdDbContext
        _logger = logger;
    }

    // ── GET /databases — all DBs with key counts ──────────────────────────────
    [HttpGet("databases")]
    public IActionResult GetDatabases()
    {
        var result = DbConfigs.Select(cfg =>
        {
            long keyCount = 0;
            try { keyCount = _server.DatabaseSize(cfg.DbIndex); } catch { }
            return new
            {
                dbIndex = cfg.DbIndex,
                label = cfg.Label,
                description = cfg.Description,
                keyCount,
                groupCount = cfg.DefaultGroups.Length,
            };
        });
        return Ok(result);
    }

    // ── GET /db/{db}/stats — groups with key counts for a DB ─────────────────
    [HttpGet("db/{db:int}/stats")]
    public IActionResult GetDbStats(int db)
    {
        if (db < 0 || db > 2) return BadRequest(new { message = "DbIndex must be 0–2" });

        var cfg = DbConfigs[db];
        var groups = cfg.DefaultGroups.Select(g =>
        {
            int keyCount = 0;
            try { keyCount = _server.Keys(database: db, pattern: $"{g.Prefix}*").Count(); } catch { }
            return new
            {
                prefix = g.Prefix,
                name = g.Name,
                description = g.Description,
                defaultTtl = g.DefaultTtl,
                keyCount,
            };
        }).ToList();

        // Also discover un-mapped prefixes via SCAN (show as "Other")
        try
        {
            var knownPrefixes = cfg.DefaultGroups.Select(g => g.Prefix).ToHashSet();
            var otherCount = _server.Keys(database: db, pattern: "*")
                .Count(k => !knownPrefixes.Any(p => k.ToString().StartsWith(p)));
            if (otherCount > 0)
                groups.Add(new { prefix = "*", name = "อื่นๆ", description = "Key ที่ยังไม่ได้จัดกลุ่ม", defaultTtl = "—", keyCount = otherCount });
        }
        catch { /* Redis scan errors are non-fatal */ }

        return Ok(groups);
    }

    // ── GET /db/{db}/keys?prefix={prefix} ────────────────────────────────────
    [HttpGet("db/{db:int}/keys")]
    public async Task<IActionResult> GetKeys(int db, [FromQuery] string prefix)
    {
        if (db < 0 || db > 2) return BadRequest(new { message = "DbIndex must be 0–2" });
        if (string.IsNullOrWhiteSpace(prefix)) return BadRequest(new { message = "กรุณาระบุ prefix" });

        var redisDb = _redis.GetDatabase(db);
        var definitions = await _db.CacheDefinitions
            .Where(d => d.IsActive && d.DbIndex == db)
            .ToListAsync();

        var pattern = prefix == "*" ? "*" : $"{prefix}*";
        var allKeys = _server.Keys(database: db, pattern: pattern).ToList();
        var result = new List<object>();

        foreach (var key in allKeys.Take(200))
        {
            TimeSpan? ttl = null;
            try { ttl = await redisDb.KeyTimeToLiveAsync(key); } catch { }

            var keyStr = key.ToString();
            var matched = definitions
                .Where(d => keyStr.StartsWith(d.CacheKeyPattern, StringComparison.OrdinalIgnoreCase))
                .MaxBy(d => d.CacheKeyPattern.Length);

            result.Add(new
            {
                key = keyStr,
                ttlSeconds = ttl?.TotalSeconds ?? -1,
                ttlDisplay = ttl.HasValue ? FormatTtl(ttl.Value) : "ไม่มีกำหนด",
                isActive = ttl == null || ttl.Value.TotalSeconds > 0,
                expiresAt = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value).ToString("o") : (string?)null,
                description = matched?.Description ?? "",
                definitionName = matched?.Name ?? "",
                definitionId = matched?.Id,
            });
        }

        return Ok(new { prefix, dbIndex = db, totalFound = allKeys.Count, items = result });
    }

    // ── DELETE /db/{db}/key?key={key} ────────────────────────────────────────
    [HttpDelete("db/{db:int}/key")]
    public async Task<IActionResult> DeleteKey(int db, [FromQuery] string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "กรุณาระบุ key" });
        var redisDb = _redis.GetDatabase(db);
        var deleted = await redisDb.KeyDeleteAsync(key);
        _logger.LogWarning("[Cache] Key deleted: DB{Db} '{Key}' by {User}", db, key, User.Identity?.Name);
        return Ok(new { success = deleted, key, dbIndex = db, message = deleted ? "ลบ key สำเร็จ" : "ไม่พบ key" });
    }

    // ── DELETE /db/{db}/group?prefix={prefix} ────────────────────────────────
    [HttpDelete("db/{db:int}/group")]
    public async Task<IActionResult> DeleteGroup(int db, [FromQuery] string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return BadRequest(new { message = "กรุณาระบุ prefix" });
        var redisDb = _redis.GetDatabase(db);
        var pattern = prefix == "*" ? "*" : $"{prefix}*";
        var keys = _server.Keys(database: db, pattern: pattern).ToArray();
        if (keys.Length > 0) await redisDb.KeyDeleteAsync(keys);
        _logger.LogWarning("[Cache] Group cleared: DB{Db} prefix='{Prefix}' deleted {Count} keys by {User}",
            db, prefix, keys.Length, User.Identity?.Name);
        return Ok(new { success = true, prefix, dbIndex = db, deletedCount = keys.Length });
    }

    // ── DELETE /db/{db}/flush — flush entire DB (dangerous) ──────────────────
    [HttpDelete("db/{db:int}/flush")]
    public async Task<IActionResult> FlushDb(int db, [FromHeader(Name = "X-Confirm-Flush")] string? confirm)
    {
        if (confirm != "yes")
            return BadRequest(new { message = "ต้องส่ง header X-Confirm-Flush: yes" });
        if (db < 0 || db > 2) return BadRequest(new { message = "DbIndex must be 0–2" });
        if (db == 1)
            return BadRequest(new { message = "ไม่สามารถ flush DB 1 (Auth Sessions) ผ่าน API นี้ได้ ใช้ redis-cli -n 1 FLUSHDB โดยตรง" });

        try
        {
            await _server.FlushDatabaseAsync(db);
            _logger.LogCritical("[Cache] DB{Db} flushed by {User}", db, User.Identity?.Name);
            return Ok(new { success = true, dbIndex = db, message = $"Flush DB {db} สำเร็จ" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Flush ล้มเหลว", error = ex.Message });
        }
    }

    // ── GET /metrics — global Redis server metrics ────────────────────────────
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics()
    {
        try
        {
            var info = await _server.InfoAsync();
            var dict = info.SelectMany(g => g).ToDictionary(kv => kv.Key, kv => kv.Value);

            long hits = dict.TryGetValue("keyspace_hits", out var h) ? long.Parse(h) : 0;
            long misses = dict.TryGetValue("keyspace_misses", out var m) ? long.Parse(m) : 0;
            double hitRate = (hits + misses) > 0 ? Math.Round((double)hits / (hits + misses) * 100, 2) : 0;

            // Per-DB key counts from keyspace section
            var keyspaceInfo = await _server.InfoAsync("keyspace");
            var dbKeyCounts = new Dictionary<int, long>();
            foreach (var group in keyspaceInfo)
                foreach (var kv in group)
                    if (kv.Key.StartsWith("db") && int.TryParse(kv.Key[2..], out var dbIdx))
                    {
                        // Value format: "keys=12,expires=3,avg_ttl=123456"
                        var parts = kv.Value.Split(',');
                        var keysPart = parts.FirstOrDefault(p => p.StartsWith("keys="));
                        if (keysPart != null && long.TryParse(keysPart[5..], out var cnt))
                            dbKeyCounts[dbIdx] = cnt;
                    }

            return Ok(new
            {
                usedMemory = dict.GetValueOrDefault("used_memory_human", "N/A"),
                usedMemoryPeak = dict.GetValueOrDefault("used_memory_peak_human", "N/A"),
                memFragmentationRatio = dict.TryGetValue("mem_fragmentation_ratio", out var frag)
                    ? double.Parse(frag) : 0,
                connectedClients = dict.TryGetValue("connected_clients", out var cl)
                    ? int.Parse(cl) : 0,
                opsPerSec = dict.TryGetValue("instantaneous_ops_per_sec", out var ops)
                    ? int.Parse(ops) : 0,
                keyspaceHits = hits,
                keyspaceMisses = misses,
                hitRate,
                uptimeSeconds = dict.TryGetValue("uptime_in_seconds", out var up)
                    ? long.Parse(up) : 0,
                totalCommandsProcessed = dict.TryGetValue("total_commands_processed", out var tc)
                    ? long.Parse(tc) : 0,
                redisVersion = dict.GetValueOrDefault("redis_version", "N/A"),
                dbKeyCounts,
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "ดึง metrics ไม่ได้", error = ex.Message });
        }
    }

    // ── GET /status ───────────────────────────────────────────────────────────
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        try
        {
            var endpoint = _redis.GetEndPoints().FirstOrDefault()?.ToString();
            return Ok(new
            {
                connected = _redis.IsConnected,
                endpoint,
                timestamp = DateTimeOffset.UtcNow,
            });
        }
        catch { return Ok(new { connected = false, endpoint = (string?)null, timestamp = DateTimeOffset.UtcNow }); }
    }

    // ── CacheDefinition CRUD ──────────────────────────────────────────────────

    [HttpGet("definitions")]
    public async Task<IActionResult> GetDefinitions([FromQuery] int? db)
    {
        var query = _db.CacheDefinitions.AsQueryable();
        if (db.HasValue) query = query.Where(d => d.DbIndex == db.Value);
        var list = await query.OrderBy(d => d.DbIndex).ThenBy(d => d.GroupPrefix).ThenBy(d => d.CacheKeyPattern).ToListAsync();
        return Ok(list);
    }

    [HttpPost("definitions")]
    public async Task<IActionResult> CreateDefinition([FromBody] CacheDefinitionDto dto)
    {
        var def = new CacheDefinition
        {
            CacheKeyPattern = dto.CacheKeyPattern.Trim(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            GroupPrefix = dto.GroupPrefix.Trim(),
            DbIndex = dto.DbIndex,
            SuggestedTtlMinutes = dto.SuggestedTtlMinutes,
            IsActive = dto.IsActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.CacheDefinitions.Add(def);
        await _db.SaveChangesAsync();
        return Ok(def);
    }

    [HttpPut("definitions/{id:int}")]
    public async Task<IActionResult> UpdateDefinition(int id, [FromBody] CacheDefinitionDto dto)
    {
        var existing = await _db.CacheDefinitions.FindAsync(id);
        if (existing == null) return NotFound();
        existing.CacheKeyPattern = dto.CacheKeyPattern.Trim();
        existing.Name = dto.Name.Trim();
        existing.Description = dto.Description?.Trim();
        existing.GroupPrefix = dto.GroupPrefix.Trim();
        existing.DbIndex = dto.DbIndex;
        existing.SuggestedTtlMinutes = dto.SuggestedTtlMinutes;
        existing.IsActive = dto.IsActive;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("definitions/{id:int}")]
    public async Task<IActionResult> DeleteDefinition(int id)
    {
        var existing = await _db.CacheDefinitions.FindAsync(id);
        if (existing == null) return NotFound();
        _db.CacheDefinitions.Remove(existing);
        await _db.SaveChangesAsync();
        return Ok(new { message = "ลบคำอธิบายสำเร็จ" });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string FormatTtl(TimeSpan ttl)
    {
        if (ttl.TotalDays >= 1) return $"{(int)ttl.TotalDays}ว {ttl.Hours}ช {ttl.Minutes}น";
        if (ttl.TotalHours >= 1) return $"{(int)ttl.TotalHours}ช {ttl.Minutes}น {ttl.Seconds}ว";
        if (ttl.TotalMinutes >= 1) return $"{(int)ttl.TotalMinutes}น {ttl.Seconds}ว";
        return $"{ttl.Seconds} วินาที";
    }

    private record RedisDbConfig(int DbIndex, string Label, string Description, GroupDef[] DefaultGroups);
    private record GroupDef(string Prefix, string Name, string Description, string DefaultTtl);
}

public record CacheDefinitionDto(
    string CacheKeyPattern,
    string Name,
    string? Description,
    string GroupPrefix,
    int DbIndex,
    int? SuggestedTtlMinutes,
    bool IsActive
);
