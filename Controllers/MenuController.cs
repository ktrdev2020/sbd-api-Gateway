using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using Gateway.Services;

namespace Gateway.Controllers;

// ─── Shared DTO (used by both MenuController and AdminMenuController) ─────────
public record MenuItemDto(
    int Id,
    string Role,
    string ItemKey,
    string Label,
    string Icon,
    string RouteTemplate,
    int SortOrder,
    bool IsActive,
    bool ExactMatch,
    string? Badge,
    string? Gradient
);

/// <summary>
/// Menu endpoints — active core menu items per role.
/// Authenticated roles: Redis-cached (5 min) with automatic DB fallback.
/// Guest (public): AllowAnonymous — no JWT required, used by the public portal.
/// </summary>
[ApiController]
[Route("api/v1/menu")]
[Authorize]
public class MenuController(SbdDbContext db, ICacheService cache, ILogger<MenuController> logger) : ControllerBase
{
    private static string CacheKey(string role) => $"menu:core:{role}";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static readonly string[] AllowedRoles =
        ["SuperAdmin", "AreaAdmin", "SchoolAdmin", "Teacher", "Student"];

    // ── Authenticated roles ──────────────────────────────────────────────────
    [HttpGet("core/{role}")]
    public async Task<ActionResult<List<MenuItemDto>>> GetCoreItems(string role)
    {
        if (!AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            return BadRequest("Unknown role");

        return Ok(await FetchMenuItems(role));
    }

    // ── Public Guest menu — no authentication required ───────────────────────
    [HttpGet("core/guest")]
    [AllowAnonymous]
    public async Task<ActionResult<List<MenuItemDto>>> GetGuestItems()
        => Ok(await FetchMenuItems("Guest"));

    // ── Shared helper ────────────────────────────────────────────────────────
    private async Task<List<MenuItemDto>> FetchMenuItems(string role)
    {
        // 1. Redis
        try
        {
            var cached = await cache.GetAsync<List<MenuItemDto>>(CacheKey(role));
            if (cached is { Count: > 0 }) return cached;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Menu] Redis unavailable — falling through to DB for {Role}", role);
        }

        // 2. DB
        try
        {
            var items = await db.Database
                .SqlQuery<MenuItemDto>($"""
                    SELECT "Id","Role","ItemKey","Label","Icon","RouteTemplate",
                           "SortOrder","IsActive","ExactMatch","Badge","Gradient"
                    FROM   "MenuItems"
                    WHERE  "Role" = {role}
                      AND  "IsActive" = TRUE
                    ORDER  BY "SortOrder"
                """)
                .ToListAsync();

            // 3. Populate Redis
            try { await cache.SetAsync(CacheKey(role), items, Ttl); }
            catch { /* cache write failure is non-fatal */ }

            return items;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Menu] DB query failed for role {Role}", role);
            return [];
        }
    }
}
