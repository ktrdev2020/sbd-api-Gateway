using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gateway.Services;

namespace Gateway.Controllers;

/// <summary>
/// SuperAdmin-owned system-wide settings (active academic year, term).
/// Reads = anonymous (frontend bootstrap merges into ConfigService);
/// writes = SuperAdmin only.
///
/// Storage: Redis hash `system:settings`. Whitelist of allowed keys keeps
/// the surface tight — adding a new setting means updating both the
/// whitelist and the frontend AppConfig type. No DB schema needed; the
/// values are pure configuration, not transactional records.
/// </summary>
[ApiController]
[Route("api/v1")]
public class SystemSettingsController(ICacheService cache, ILogger<SystemSettingsController> logger) : ControllerBase
{
    private const string CacheKey = "system:settings:v1";

    /// <summary>
    /// Whitelist of writable / readable keys + their JSON-serialized default value.
    /// Frontend AppConfig type must mirror this list.
    /// </summary>
    private static readonly Dictionary<string, object> Defaults = new()
    {
        ["currentAcademicYear"] = 2569,
        ["currentTerm"] = 1,
    };

    public record SystemSettingsDto(int CurrentAcademicYear, int CurrentTerm);
    public record UpdateSystemSettingsRequest(int? CurrentAcademicYear, int? CurrentTerm);

    /// <summary>Public read — anyone with a valid client can fetch.</summary>
    [HttpGet("system-settings")]
    [AllowAnonymous]
    public async Task<ActionResult<SystemSettingsDto>> Get()
    {
        var stored = await cache.GetAsync<Dictionary<string, object>>(CacheKey) ?? new();
        return Ok(new SystemSettingsDto(
            CurrentAcademicYear: GetInt(stored, "currentAcademicYear", (int)Defaults["currentAcademicYear"]),
            CurrentTerm: GetInt(stored, "currentTerm", (int)Defaults["currentTerm"])
        ));
    }

    /// <summary>SuperAdmin only — partial update; null fields keep current value.</summary>
    [HttpPut("admin/system-settings")]
    [Authorize(Roles = "super_admin,SuperAdmin")]
    public async Task<ActionResult<SystemSettingsDto>> Update(
        [FromBody] UpdateSystemSettingsRequest req)
    {
        var stored = await cache.GetAsync<Dictionary<string, object>>(CacheKey) ?? new();

        if (req.CurrentAcademicYear is int year)
        {
            if (year < 2500 || year > 2600)
                return BadRequest(new { message = "currentAcademicYear ต้องอยู่ระหว่าง 2500–2600" });
            stored["currentAcademicYear"] = year;
        }
        if (req.CurrentTerm is int term)
        {
            if (term is not (1 or 2))
                return BadRequest(new { message = "currentTerm ต้องเป็น 1 หรือ 2" });
            stored["currentTerm"] = term;
        }

        await cache.SetAsync(CacheKey, stored, TimeSpan.FromDays(3650));
        logger.LogInformation("System settings updated by {User}: AY={AY} Term={Term}",
            User.Identity?.Name, stored.GetValueOrDefault("currentAcademicYear"), stored.GetValueOrDefault("currentTerm"));

        return Ok(new SystemSettingsDto(
            CurrentAcademicYear: GetInt(stored, "currentAcademicYear", (int)Defaults["currentAcademicYear"]),
            CurrentTerm: GetInt(stored, "currentTerm", (int)Defaults["currentTerm"])
        ));
    }

    private static int GetInt(Dictionary<string, object> map, string key, int fallback)
    {
        if (!map.TryGetValue(key, out var raw) || raw is null) return fallback;
        return raw switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number => je.GetInt32(),
            _ => fallback
        };
    }
}
