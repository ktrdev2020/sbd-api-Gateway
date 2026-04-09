using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Admin endpoints for user management.
/// Reads Users + risk scores directly from the shared DB (ADR-009: Gateway reads DB directly).
/// </summary>
[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
public class UserAdminController(SbdDbContext db) : ControllerBase
{
    /// <summary>
    /// GET /api/v1/admin/users — paginated user list with inline risk score.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        var query = db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(s) ||
                u.Username.ToLower().Contains(s) ||
                u.Email.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.UserRoles.Any(ur => ur.Role.Code == role));
        }

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.DisplayName,
                Roles = u.UserRoles.Select(ur => ur.Role.Code).ToList(),
                ActiveRole = u.UserRoles.Select(ur => ur.Role.Code).FirstOrDefault() ?? "Student",
                SchoolId = (int?)u.UserRoles
                    .Where(ur => ur.ScopeType == "School" && ur.ScopeId.HasValue)
                    .Select(ur => ur.ScopeId)
                    .FirstOrDefault(),
                AreaId = (int?)u.UserRoles
                    .Where(ur => ur.ScopeType == "Area" && ur.ScopeId.HasValue)
                    .Select(ur => ur.ScopeId)
                    .FirstOrDefault(),
                Provider = u.LoginProviders.Select(lp => lp.Provider).FirstOrDefault() ?? "local",
                u.CreatedAt,
            })
            .ToListAsync(ct);

        // Join risk scores (left join from separate table)
        var userIds = users.Select(u => u.Id).ToList();
        var riskMap = await db.UserRiskScores
            .AsNoTracking()
            .Where(r => userIds.Contains(r.UserId))
            .Select(r => new { r.UserId, r.Score, r.Level, r.LastScoredAt })
            .ToDictionaryAsync(r => r.UserId, ct);

        var result = users.Select(u =>
        {
            riskMap.TryGetValue(u.Id, out var risk);
            return new
            {
                u.Id,
                u.Username,
                u.Email,
                u.DisplayName,
                u.Roles,
                u.ActiveRole,
                u.SchoolId,
                u.AreaId,
                u.Provider,
                u.CreatedAt,
                RiskScore = risk?.Score,
                RiskLevel = risk?.Level ?? "low",
                RiskScoredAt = risk?.LastScoredAt,
            };
        });

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            Items = result,
        });
    }

    /// <summary>
    /// GET /api/v1/admin/users/{id} — single user detail.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUser(int id, CancellationToken ct = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null) return NotFound();

        var risk = await db.UserRiskScores
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == id, ct);

        return Ok(new
        {
            user.Id,
            user.Username,
            user.Email,
            user.DisplayName,
            Roles = user.UserRoles.Select(ur => ur.Role.Code).ToList(),
            ActiveRole = user.UserRoles.Select(ur => ur.Role.Code).FirstOrDefault() ?? "Student",
            Provider = user.LoginProviders.Select(lp => lp.Provider).FirstOrDefault() ?? "local",
            user.CreatedAt,
            Risk = risk is null ? null : new
            {
                risk.Score,
                risk.Level,
                risk.FactorsJson,
                risk.LastScoredAt,
            },
        });
    }
}
