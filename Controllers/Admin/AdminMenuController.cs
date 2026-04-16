using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using Gateway.Services;
using Gateway.Controllers; // MenuItemDto

namespace Gateway.Controllers.Admin;

// ─── Request / response records ───────────────────────────────────────────────

public record CreateMenuItemRequest(
    string Role,
    string ItemKey,
    string Label,
    string Icon,
    string RouteTemplate,
    int SortOrder,
    bool ExactMatch,
    string? Badge,
    string? Gradient
);

public record UpdateMenuItemRequest(
    string Label,
    string Icon,
    string RouteTemplate,
    bool IsActive,
    bool ExactMatch,
    string? Badge,
    string? Gradient
);

public record ReorderRequest(List<ReorderEntry> Items);
public record ReorderEntry(int Id, int SortOrder);

/// <summary>
/// SuperAdmin-only CRUD for sidebar menu items.
/// Uses EF Core parameterized SQL (ExecuteSqlAsync) throughout — no SQL injection risk.
/// Every mutation invalidates the Redis cache for the affected role.
/// </summary>
[ApiController]
[Route("api/v1/admin/menu-items")]
[Authorize(Roles = "SuperAdmin,super_admin")]
public class AdminMenuController(
    SbdDbContext db,
    ICacheService cache,
    ILogger<AdminMenuController> logger) : ControllerBase
{
    private static string CacheKey(string role) => $"menu:core:{role}";

    // ── GET all (all roles, for management UI) ─────────────────────────────
    [HttpGet]
    public async Task<ActionResult<List<MenuItemDto>>> GetAll([FromQuery] string? role = null)
    {
        try
        {
            List<MenuItemDto> items = string.IsNullOrEmpty(role)
                ? await db.Database
                    .SqlQuery<MenuItemDto>($"""
                        SELECT "Id","Role","ItemKey","Label","Icon","RouteTemplate",
                               "SortOrder","IsActive","ExactMatch","Badge","Gradient"
                        FROM   "MenuItems"
                        ORDER  BY "Role", "SortOrder"
                    """)
                    .ToListAsync()
                : await db.Database
                    .SqlQuery<MenuItemDto>($"""
                        SELECT "Id","Role","ItemKey","Label","Icon","RouteTemplate",
                               "SortOrder","IsActive","ExactMatch","Badge","Gradient"
                        FROM   "MenuItems"
                        WHERE  "Role" = {role}
                        ORDER  BY "SortOrder"
                    """)
                    .ToListAsync();

            return Ok(items);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AdminMenu] GetAll failed");
            return StatusCode(500, "Failed to load menu items");
        }
    }

    // ── POST create ────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<MenuItemDto>> Create([FromBody] CreateMenuItemRequest req)
    {
        try
        {
            // ExecuteSqlAsync uses FormattableString — all interpolated values are parameters
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "MenuItems"
                    ("Role","ItemKey","Label","Icon","RouteTemplate","SortOrder","IsActive","ExactMatch","Badge","Gradient","CreatedAt")
                VALUES
                    ({req.Role},{req.ItemKey},{req.Label},{req.Icon},{req.RouteTemplate},
                     {req.SortOrder},TRUE,{req.ExactMatch},{req.Badge},{req.Gradient},NOW())
                ON CONFLICT ("Role","ItemKey") DO NOTHING
                """);

            await InvalidateCache(req.Role);
            logger.LogInformation("[AdminMenu] Created {Role}/{Key}", req.Role, req.ItemKey);

            var created = await db.Database
                .SqlQuery<MenuItemDto>($"""
                    SELECT "Id","Role","ItemKey","Label","Icon","RouteTemplate",
                           "SortOrder","IsActive","ExactMatch","Badge","Gradient"
                    FROM "MenuItems"
                    WHERE "Role" = {req.Role} AND "ItemKey" = {req.ItemKey}
                    """)
                .FirstOrDefaultAsync();

            return created is null ? StatusCode(409, "Item already exists") : Ok(created);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AdminMenu] Create failed");
            return StatusCode(500, "Failed to create menu item");
        }
    }

    // ── PUT update ─────────────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<ActionResult<MenuItemDto>> Update(int id, [FromBody] UpdateMenuItemRequest req)
    {
        try
        {
            var affected = await db.Database.ExecuteSqlAsync($"""
                UPDATE "MenuItems"
                SET "Label"         = {req.Label},
                    "Icon"          = {req.Icon},
                    "RouteTemplate" = {req.RouteTemplate},
                    "IsActive"      = {req.IsActive},
                    "ExactMatch"    = {req.ExactMatch},
                    "Badge"         = {req.Badge},
                    "Gradient"      = {req.Gradient},
                    "UpdatedAt"     = NOW()
                WHERE "Id" = {id}
                """);

            if (affected == 0) return NotFound();

            var roleRow = await db.Database
                .SqlQuery<RoleOnly>($"""SELECT "Role" FROM "MenuItems" WHERE "Id" = {id}""")
                .FirstOrDefaultAsync();
            if (roleRow is not null) await InvalidateCache(roleRow.Role);

            var updated = await db.Database
                .SqlQuery<MenuItemDto>($"""
                    SELECT "Id","Role","ItemKey","Label","Icon","RouteTemplate",
                           "SortOrder","IsActive","ExactMatch","Badge","Gradient"
                    FROM "MenuItems" WHERE "Id" = {id}
                    """)
                .FirstOrDefaultAsync();

            return updated is null ? NotFound() : Ok(updated);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AdminMenu] Update {Id} failed", id);
            return StatusCode(500, "Failed to update menu item");
        }
    }

    // ── PUT toggle active ──────────────────────────────────────────────────
    [HttpPut("{id:int}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        try
        {
            var roleRow = await db.Database
                .SqlQuery<RoleOnly>($"""SELECT "Role" FROM "MenuItems" WHERE "Id" = {id}""")
                .FirstOrDefaultAsync();
            if (roleRow is null) return NotFound();

            await db.Database.ExecuteSqlAsync($"""
                UPDATE "MenuItems"
                SET "IsActive" = NOT "IsActive", "UpdatedAt" = NOW()
                WHERE "Id" = {id}
                """);

            await InvalidateCache(roleRow.Role);
            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AdminMenu] Toggle {Id} failed", id);
            return StatusCode(500, "Failed to toggle menu item");
        }
    }

    // ── POST reorder (batch SortOrder update) ─────────────────────────────
    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder([FromBody] ReorderRequest req)
    {
        if (req.Items.Count == 0) return BadRequest("Empty reorder list");

        try
        {
            var affectedRoles = new HashSet<string>();

            foreach (var item in req.Items)
            {
                var roleRow = await db.Database
                    .SqlQuery<RoleOnly>($"""SELECT "Role" FROM "MenuItems" WHERE "Id" = {item.Id}""")
                    .FirstOrDefaultAsync();
                if (roleRow is not null) affectedRoles.Add(roleRow.Role);

                await db.Database.ExecuteSqlAsync($"""
                    UPDATE "MenuItems" SET "SortOrder" = {item.SortOrder}, "UpdatedAt" = NOW()
                    WHERE "Id" = {item.Id}
                    """);
            }

            foreach (var role in affectedRoles) await InvalidateCache(role);
            logger.LogInformation("[AdminMenu] Reordered {Count} items", req.Items.Count);
            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AdminMenu] Reorder failed");
            return StatusCode(500, "Failed to reorder menu items");
        }
    }

    // ── DELETE (soft — sets IsActive = false) ─────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var roleRow = await db.Database
                .SqlQuery<RoleOnly>($"""SELECT "Role" FROM "MenuItems" WHERE "Id" = {id}""")
                .FirstOrDefaultAsync();
            if (roleRow is null) return NotFound();

            await db.Database.ExecuteSqlAsync($"""
                UPDATE "MenuItems" SET "IsActive" = FALSE, "UpdatedAt" = NOW()
                WHERE "Id" = {id}
                """);

            await InvalidateCache(roleRow.Role);
            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AdminMenu] Delete {Id} failed", id);
            return StatusCode(500, "Failed to delete menu item");
        }
    }

    // ── POST invalidate Redis cache for a role ─────────────────────────────
    [HttpPost("cache/{role}/invalidate")]
    public async Task<IActionResult> InvalidateCacheEndpoint(string role)
    {
        await InvalidateCache(role);
        return Ok(new { invalidated = CacheKey(role) });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────
    private async Task InvalidateCache(string role)
    {
        try { await cache.RemoveAsync(CacheKey(role)); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[AdminMenu] Cache invalidation failed for {Role}", role);
        }
    }

    private record RoleOnly(string Role);
}
