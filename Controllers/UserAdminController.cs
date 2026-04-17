using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Admin endpoints for comprehensive user management.
/// SuperAdmin / AreaAdmin / SchoolAdmin access — each role sees only its own scope.
/// SchoolAdmin access is allowed by default; area can block it via AreaPermissionPolicy
/// "user.manage_school_users" with AllowSchoolAdmin = false (opt-out model).
///</summary>
[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Roles = "super_admin,area_admin,school_admin,SuperAdmin,AreaAdmin,SchoolAdmin")]
public class UserAdminController(SbdDbContext db, ICacheService cache) : ControllerBase
{
    // ─── Cache key helpers ────────────────────────────────────────────────────
    private static readonly TimeSpan ListTtl   = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StatsTtl  = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(2);

    private const string ListPrefix   = "admin:users:list:";
    private const string StatsPrefix  = "admin:users:stats:";
    private static string DetailKey(int id) => $"admin:users:detail:{id}";

    // ─── Scope-aware cache helpers — prevents cross-caller cache pollution ────
    private static string ScopedStatsKey(string scopeTag)  => $"{StatsPrefix}{scopeTag}";
    private static string ListCacheKey(
        string scopeTag,
        string? search, string? role, string? provider, string? district,
        int? schoolId, string? status, string? positionType, string? subjectArea,
        int page, int pageSize)
    {
        var raw = $"{scopeTag}|{search}|{role}|{provider}|{district}|{schoolId}|{status}|{positionType}|{subjectArea}|{page}|{pageSize}";
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
        return $"{ListPrefix}{hash}";
    }

    // ─── Caller scope resolution ──────────────────────────────────────────────
    // Returns null when the caller's role can't be resolved or the school_admin's
    // area has not granted them user-management permission.

    private record CallerScope(
        string Role,
        int? AreaId,
        int? SchoolId,
        IReadOnlyList<int> AllowedSchoolIds,
        string CacheTag);

    private async Task<CallerScope?> GetCallerScopeAsync(CancellationToken ct = default)
    {
        // super_admin — unrestricted
        if (User.IsInRole("super_admin") || User.IsInRole("SuperAdmin"))
            return new CallerScope("super_admin", null, null, Array.Empty<int>(), "global");

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(userIdStr, out var userId)) return null;

        // area_admin — scoped to their area's schools
        var areaRole = await db.UserRoles
            .Include(ur => ur.Role)
            .FirstOrDefaultAsync(ur =>
                ur.UserId == userId
                && ur.Role.Code == "area_admin"
                && ur.ScopeType == "Area"
                && ur.ScopeId.HasValue, ct);

        if (areaRole is not null)
        {
            var areaId = areaRole.ScopeId!.Value;
            var schoolIds = await db.Schools
                .AsNoTracking()
                .Where(s => s.AreaId == areaId)
                .Select(s => s.Id)
                .ToListAsync(ct);
            return new CallerScope("area_admin", areaId, null, schoolIds, $"area:{areaId}");
        }

        // school_admin — allowed by default; denied only when the area has explicitly
        // set AllowSchoolAdmin = false for "user.manage_school_users".
        // No policy entry = permission granted (opt-out model, not opt-in).
        var schoolRole = await db.UserRoles
            .Include(ur => ur.Role)
            .FirstOrDefaultAsync(ur =>
                ur.UserId == userId
                && ur.Role.Code == "school_admin"
                && ur.ScopeType == "School"
                && ur.ScopeId.HasValue, ct);

        if (schoolRole is not null)
        {
            var schoolId = schoolRole.ScopeId!.Value;
            var school   = await db.Schools.AsNoTracking()
                .Select(s => new { s.Id, s.AreaId })
                .FirstOrDefaultAsync(s => s.Id == schoolId, ct);
            if (school is null) return null;

            // Opt-out model: denied only if the area has explicitly blocked it.
            var explicitlyDenied = await db.AreaPermissionPolicies
                .AnyAsync(p =>
                    p.AreaId == school.AreaId
                    && p.PermissionCode == "user.manage_school_users"
                    && !p.AllowSchoolAdmin, ct);

            if (explicitlyDenied) return null;
            return new CallerScope("school_admin", null, schoolId, new[] { schoolId }, $"school:{schoolId}");
        }

        return null;
    }

    // Returns true when the caller (area_admin / school_admin) is allowed to act on the given user.
    // For super_admin the check is always skipped (AllowedSchoolIds empty = unrestricted).
    private static bool IsInScope(CallerScope scope, IEnumerable<int> userSchoolIds)
    {
        if (scope.Role == "super_admin") return true;
        if (scope.AllowedSchoolIds.Count == 0) return false;
        return userSchoolIds.Any(id => scope.AllowedSchoolIds.Contains(id));
    }

    // Applies scope restriction to a User query (before counting / fetching).
    private static IQueryable<User> ApplyScopeFilter(IQueryable<User> query, CallerScope scope)
    {
        if (scope.Role == "super_admin") return query;
        if (scope.AllowedSchoolIds.Count == 0) return query.Where(_ => false);
        return query.Where(u =>
            u.Personnel != null &&
            u.Personnel.SchoolAssignments.Any(sa =>
                sa.IsPrimary && scope.AllowedSchoolIds.Contains(sa.SchoolId)));
    }

    // ─── Cache helpers (graceful — never throw) ───────────────────────────────
    private async Task<T?> TryGetCache<T>(string key)
    {
        try { return await cache.GetAsync<T>(key); }
        catch { return default; }
    }

    private async Task TrySetCache<T>(string key, T value, TimeSpan ttl)
    {
        try { await cache.SetAsync(key, value, ttl); }
        catch { }
    }

    private async Task TryRemoveCache(params string[] keys)
    {
        try { foreach (var k in keys) await cache.RemoveAsync(k); }
        catch { }
    }

    private async Task TryInvalidateListCache()
    {
        try { await cache.RemoveByPrefixAsync(ListPrefix); }
        catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/admin/users — paginated list with rich filters
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] string? provider = null,
        [FromQuery] string? district = null,
        [FromQuery] int? schoolId = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? hasPersonnel = null,
        [FromQuery] string? positionType = null,
        [FromQuery] string? subjectArea = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(ct);
        if (scope is null) return Forbid();

        // school_admin can only browse their own school — ignore schoolId param
        if (scope.Role == "school_admin") schoolId = scope.SchoolId;

        var cacheKey = ListCacheKey(scope.CacheTag, search, role, provider, district, schoolId, status, positionType, subjectArea, page, pageSize);
        var cached   = await TryGetCache<object>(cacheKey);
        if (cached is not null) return Ok(cached);

        var query = ApplyScopeFilter(db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.LoginProviders)
            .Include(u => u.Personnel)
                .ThenInclude(p => p!.TitlePrefix)
            .Include(u => u.Personnel)
                .ThenInclude(p => p!.PositionType)
            .Include(u => u.Personnel)
                .ThenInclude(p => p!.SchoolAssignments.Where(sa => sa.IsPrimary))
                    .ThenInclude(sa => sa.School)
                        .ThenInclude(s => s.Address!)
                            .ThenInclude(a => a.SubDistrict)
                                .ThenInclude(sd => sd.District)
            .AsQueryable(), scope);

        // ── Text search ────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(s) ||
                u.Username.ToLower().Contains(s) ||
                u.Email.ToLower().Contains(s) ||
                (u.Personnel != null && (
                    u.Personnel.FirstName.ToLower().Contains(s) ||
                    u.Personnel.LastName.ToLower().Contains(s))));
        }

        // ── Role filter ────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.UserRoles.Any(ur => ur.Role.Code == role));
        }

        // ── Provider filter ────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(u => u.LoginProviders.Any(lp => lp.Provider == provider));
        }

        // ── Status filter ──────────────────────────────────────────────────
        if (status == "active")
            query = query.Where(u => u.IsActive);
        else if (status == "inactive")
            query = query.Where(u => !u.IsActive);

        // ── Has-personnel filter ───────────────────────────────────────────
        if (hasPersonnel == true)
            query = query.Where(u => u.Personnel != null);
        else if (hasPersonnel == false)
            query = query.Where(u => u.Personnel == null);

        // ── Position type filter (by PositionType.NameTh) ─────────────────────
        if (!string.IsNullOrWhiteSpace(positionType))
        {
            query = query.Where(u =>
                u.Personnel != null &&
                u.Personnel.PositionType != null &&
                u.Personnel.PositionType.NameTh == positionType);
        }

        // ── Subject area filter ────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(subjectArea))
        {
            query = query.Where(u =>
                u.Personnel != null &&
                u.Personnel.SubjectArea == subjectArea);
        }

        // ── District filter (via Personnel→PrimaryAssignment→School→Address→District) ──
        if (!string.IsNullOrWhiteSpace(district))
        {
            var districtName = district.Trim();
            var schoolIds = await db.PersonnelSchoolAssignments
                .AsNoTracking()
                .Where(sa =>
                    sa.IsPrimary &&
                    sa.School.Address != null &&
                    sa.School.Address.SubDistrict.District.NameTh == districtName)
                .Select(sa => sa.SchoolId)
                .Distinct()
                .ToListAsync(ct);

            var personnelIds = await db.PersonnelSchoolAssignments
                .AsNoTracking()
                .Where(sa => sa.IsPrimary && schoolIds.Contains(sa.SchoolId))
                .Select(sa => sa.PersonnelId)
                .Distinct()
                .ToListAsync(ct);

            query = query.Where(u => u.Personnel != null && personnelIds.Contains(u.Personnel!.Id));
        }

        // ── School filter ──────────────────────────────────────────────────
        if (schoolId.HasValue)
        {
            query = query.Where(u =>
                u.Personnel != null &&
                u.Personnel!.SchoolAssignments.Any(sa => sa.SchoolId == schoolId.Value && sa.IsPrimary));
        }

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // ── Join risk scores ───────────────────────────────────────────────
        var userIds = users.Select(u => u.Id).ToList();
        var riskMap = await db.UserRiskScores
            .AsNoTracking()
            .Where(r => userIds.Contains(r.UserId))
            .ToDictionaryAsync(r => r.UserId, ct);

        var items = users.Select(u =>
        {
            riskMap.TryGetValue(u.Id, out var risk);
            var primaryAssignment = u.Personnel?.SchoolAssignments
                .FirstOrDefault(sa => sa.IsPrimary);
            var school = primaryAssignment?.School;
            var district2 = school?.Address?.SubDistrict?.District;

            return new
            {
                u.Id,
                u.Username,
                u.Email,
                u.DisplayName,
                u.IsActive,
                u.AvatarThumbnailUrl,
                u.AvatarVersion,
                u.CreatedAt,
                u.LastLoginAt,
                u.UpdatedAt,
                PersonnelId = u.Personnel?.Id,
                PersonnelType = u.Personnel?.PersonnelType,
                PersonnelFirstName = u.Personnel?.FirstName,
                PersonnelLastName = u.Personnel?.LastName,
                PersonnelTitle = u.Personnel?.TitlePrefix?.NameTh,
                SchoolName = school?.NameTh,
                DistrictName = district2?.NameTh,
                Roles = u.UserRoles.Select(ur => ur.Role.Code).ToList(),
                HasLocal = u.LoginProviders.Any(lp => lp.Provider == "local"),
                HasGoogle = u.LoginProviders.Any(lp => lp.Provider == "google"),
                HasThaiId = u.LoginProviders.Any(lp => lp.Provider == "thaid"),
                RiskScore = risk?.Score,
                RiskLevel = risk?.Level ?? "low",
            };
        });

        var result = new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            Items = items,
        };
        await TrySetCache(cacheKey, result, ListTtl);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/admin/users/stats
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(ct);
        if (scope is null) return Forbid();

        var statsKey = ScopedStatsKey(scope.CacheTag);
        var cachedStats = await TryGetCache<object>(statsKey);
        if (cachedStats is not null) return Ok(cachedStats);

        var now = DateTimeOffset.UtcNow;
        var startOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        // Base scoped query
        var baseQuery = ApplyScopeFilter(db.Users.AsNoTracking()
            .Include(u => u.Personnel)
                .ThenInclude(p => p!.SchoolAssignments), scope);

        var total = await baseQuery.CountAsync(ct);
        var active = await baseQuery.CountAsync(u => u.IsActive, ct);
        var withPersonnel = await baseQuery.CountAsync(u => u.Personnel != null, ct);
        var withAvatar = await baseQuery.CountAsync(u => u.AvatarUrl != null, ct);
        var profileUpdated = await baseQuery.CountAsync(u => u.UpdatedAt != null, ct);
        var newThisMonth = await baseQuery.CountAsync(u => u.CreatedAt >= startOfMonth, ct);

        // Scope-aware: join through Personnel→SchoolAssignments for non-super_admin
        var scopedUserIds = scope.Role == "super_admin"
            ? null
            : await baseQuery.Select(u => u.Id).ToListAsync(ct);

        var byRole = scope.Role == "super_admin"
            ? await db.UserRoles
                .Include(ur => ur.Role)
                .GroupBy(ur => ur.Role.Code)
                .Select(g => new { Code = g.Key, Count = g.Count() })
                .ToListAsync(ct)
            : await db.UserRoles
                .Include(ur => ur.Role)
                .Where(ur => scopedUserIds!.Contains(ur.UserId))
                .GroupBy(ur => ur.Role.Code)
                .Select(g => new { Code = g.Key, Count = g.Count() })
                .ToListAsync(ct);

        var byProvider = scope.Role == "super_admin"
            ? await db.UserLoginProviders
                .GroupBy(lp => lp.Provider)
                .Select(g => new { Provider = g.Key, Count = g.Select(x => x.UserId).Distinct().Count() })
                .ToListAsync(ct)
            : await db.UserLoginProviders
                .Where(lp => scopedUserIds!.Contains(lp.UserId))
                .GroupBy(lp => lp.Provider)
                .Select(g => new { Provider = g.Key, Count = g.Select(x => x.UserId).Distinct().Count() })
                .ToListAsync(ct);

        var districtQuery = db.PersonnelSchoolAssignments
            .AsNoTracking()
            .Where(sa =>
                sa.IsPrimary &&
                sa.Personnel.UserId != null &&
                sa.School.Address != null);

        if (scope.Role != "super_admin" && scope.AllowedSchoolIds.Count > 0)
            districtQuery = districtQuery.Where(sa => scope.AllowedSchoolIds.Contains(sa.SchoolId));

        var districts = await districtQuery
            .Select(sa => new
            {
                DistrictName = sa.School.Address!.SubDistrict.District.NameTh,
                UserId = sa.Personnel.UserId!.Value,
            })
            .Distinct()
            .GroupBy(x => x.DistrictName)
            .Select(g => new { Name = g.Key, UserCount = g.Count() })
            .OrderByDescending(g => g.UserCount)
            .ToListAsync(ct);

        var statsResult = new
        {
            Total = total,
            Active = active,
            Inactive = total - active,
            WithPersonnel = withPersonnel,
            NoPersonnel = total - withPersonnel,
            WithAvatar = withAvatar,
            ProfileUpdated = profileUpdated,
            NewThisMonth = newThisMonth,
            ByRole = new
            {
                SuperAdmin = byRole.FirstOrDefault(r => r.Code == "super_admin")?.Count ?? 0,
                AreaAdmin = byRole.FirstOrDefault(r => r.Code == "area_admin")?.Count ?? 0,
                SchoolAdmin = byRole.FirstOrDefault(r => r.Code == "school_admin")?.Count ?? 0,
                Teacher = byRole.FirstOrDefault(r => r.Code == "teacher")?.Count ?? 0,
                Student = byRole.FirstOrDefault(r => r.Code == "student")?.Count ?? 0,
            },
            ByProvider = new
            {
                Local = byProvider.FirstOrDefault(p => p.Provider == "local")?.Count ?? 0,
                Google = byProvider.FirstOrDefault(p => p.Provider == "google")?.Count ?? 0,
                ThaiId = byProvider.FirstOrDefault(p => p.Provider == "thaid")?.Count ?? 0,
            },
            Districts = districts,
        };
        await TrySetCache(statsKey, statsResult, StatsTtl);
        return Ok(statsResult);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/admin/users/{id} — full detail
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUser(int id, CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(ct);
        if (scope is null) return Forbid();

        var cachedDetail = await TryGetCache<object>(DetailKey(id));
        if (cachedDetail is not null)
        {
            // For non-super_admin: scope-check the cached result via a lightweight DB check
            if (scope.Role != "super_admin")
            {
                var schoolIds = await db.PersonnelSchoolAssignments
                    .AsNoTracking()
                    .Where(sa => sa.IsPrimary && sa.Personnel.UserId == id)
                    .Select(sa => sa.SchoolId)
                    .ToListAsync(ct);
                if (!IsInScope(scope, schoolIds)) return Forbid();
            }
            return Ok(cachedDetail);
        }

        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.LoginProviders)
            .Include(u => u.Personnel)
                .ThenInclude(p => p!.TitlePrefix)
            .Include(u => u.Personnel)
                .ThenInclude(p => p!.PositionType)
            .Include(u => u.Personnel)
                .ThenInclude(p => p!.SchoolAssignments)
                    .ThenInclude(sa => sa.School)
                        .ThenInclude(s => s.Address!)
                            .ThenInclude(a => a.SubDistrict)
                                .ThenInclude(sd => sd.District)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null) return NotFound();

        // Scope guard — can this caller access this user?
        if (scope.Role != "super_admin")
        {
            var userSchoolIds = user.Personnel?.SchoolAssignments
                .Where(sa => sa.IsPrimary).Select(sa => sa.SchoolId) ?? Enumerable.Empty<int>();
            if (!IsInScope(scope, userSchoolIds)) return Forbid();
        }

        var prefs = string.IsNullOrEmpty(user.PreferencesJson)
            ? new Dictionary<string, string?>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(user.PreferencesJson) ?? new();

        var risk = await db.UserRiskScores
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == id, ct);

        var recentSessions = await db.AuthSessions
            .AsNoTracking()
            .Where(s => s.UserId == id)
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .Select(s => new
            {
                s.Id,
                s.Provider,
                s.DeviceLabel,
                s.IpAddress,
                s.CreatedAt,
                s.LastSeenAt,
                IsRevoked = s.RevokedAt != null,
                s.RevokedReason,
            })
            .ToListAsync(ct);

        // Scope names for roles
        var scopedRoles = new List<object>();
        foreach (var ur in user.UserRoles)
        {
            string? scopeName = null;
            if (ur.ScopeType == "School" && ur.ScopeId.HasValue)
            {
                scopeName = await db.Schools
                    .AsNoTracking()
                    .Where(s => s.Id == ur.ScopeId.Value)
                    .Select(s => s.NameTh)
                    .FirstOrDefaultAsync(ct);
            }
            else if (ur.ScopeType == "Area" && ur.ScopeId.HasValue)
            {
                scopeName = await db.Areas
                    .AsNoTracking()
                    .Where(a => a.Id == ur.ScopeId.Value)
                    .Select(a => a.NameTh)
                    .FirstOrDefaultAsync(ct);
            }

            scopedRoles.Add(new
            {
                RoleCode = ur.Role.Code,
                RoleName = ur.Role.Name,
                ur.ScopeType,
                ur.ScopeId,
                ScopeName = scopeName,
                ur.AssignedAt,
            });
        }

        var detailResult = new
        {
            user.Id,
            user.Username,
            user.Email,
            user.DisplayName,
            user.Phone,
            user.IsActive,
            user.AvatarUrl,
            user.AvatarThumbnailUrl,
            user.AvatarVersion,
            user.CreatedAt,
            user.LastLoginAt,
            user.UpdatedAt,
            user.PersonnelContext,
            user.PersonnelRefId,
            Personnel = user.Personnel is null ? null : new
            {
                user.Personnel.Id,
                Title = user.Personnel.TitlePrefix?.NameTh,
                user.Personnel.FirstName,
                user.Personnel.LastName,
                user.Personnel.PersonnelType,
                user.Personnel.IdCard,
                user.Personnel.PersonnelCode,
                user.Personnel.Phone,
                user.Personnel.Email,
                user.Personnel.SubjectArea,
                user.Personnel.Specialty,
                PositionType = user.Personnel.PositionType?.NameTh,
                SchoolAssignments = user.Personnel.SchoolAssignments.Select(sa => new
                {
                    sa.Id,
                    sa.SchoolId,
                    SchoolName = sa.School.NameTh,
                    DistrictName = sa.School.Address?.SubDistrict?.District?.NameTh,
                    sa.Position,
                    sa.IsPrimary,
                    sa.StartDate,
                }),
            },
            LoginProviders = user.LoginProviders.Select(lp => new
            {
                lp.Provider,
                lp.ProviderKey,
                lp.LinkedAt,
                IsEnabled = true,
            }),
            Roles = scopedRoles,
            RecentSessions = recentSessions,
            Risk = risk is null ? null : new
            {
                risk.Score,
                risk.Level,
                risk.FactorsJson,
                risk.LastScoredAt,
            },
            SocialContacts = new
            {
                LineId = prefs.GetValueOrDefault("lineId"),
                FacebookUrl = prefs.GetValueOrDefault("facebookUrl"),
                TelegramUsername = prefs.GetValueOrDefault("telegramUsername"),
            },
        };
        await TrySetCache(DetailKey(id), detailResult, DetailTtl);
        return Ok(detailResult);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/v1/admin/users/{id}
    // ─────────────────────────────────────────────────────────────────────────

    // ─── Scope guard for mutations — resolves scope and checks if target user is accessible.
    // Returns (scope, null) on success, or (null, IActionResult) when access should be denied.
    private async Task<(CallerScope? scope, IActionResult? deny)> GuardMutationAsync(
        int targetUserId, CancellationToken ct)
    {
        var scope = await GetCallerScopeAsync(ct);
        if (scope is null) return (null, Forbid());

        if (scope.Role != "super_admin")
        {
            var schoolIds = await db.PersonnelSchoolAssignments
                .AsNoTracking()
                .Where(sa => sa.IsPrimary && sa.Personnel.UserId == targetUserId)
                .Select(sa => sa.SchoolId)
                .ToListAsync(ct);
            if (!IsInScope(scope, schoolIds)) return (null, Forbid());
        }
        return (scope, null);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateUser(
        int id,
        [FromBody] UpdateUserRequest request,
        CancellationToken ct = default)
    {
        var (_, deny) = await GuardMutationAsync(id, ct);
        if (deny is not null) return deny;

        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            user.DisplayName = request.DisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(request.Email))
            user.Email = request.Email.Trim().ToLower();

        if (request.Phone is not null)
            user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;

        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await Task.WhenAll(
            TryInvalidateListCache(),
            cache.RemoveByPrefixAsync(StatsPrefix).ContinueWith(_ => { }),
            TryRemoveCache(DetailKey(id)));
        return Ok(new { user.Id, user.DisplayName, user.Email, user.Phone, user.IsActive });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/v1/admin/users/{id}/toggle-active
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPut("{id:int}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct = default)
    {
        var (_, deny) = await GuardMutationAsync(id, ct);
        if (deny is not null) return deny;

        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await Task.WhenAll(
            TryInvalidateListCache(),
            cache.RemoveByPrefixAsync(StatsPrefix).ContinueWith(_ => { }),
            TryRemoveCache(DetailKey(id)));
        return Ok(new { user.Id, user.IsActive });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/v1/admin/users/{id} — soft deactivate
    // ─────────────────────────────────────────────────────────────────────────

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeactivateUser(int id, CancellationToken ct = default)
    {
        var (_, deny) = await GuardMutationAsync(id, ct);
        if (deny is not null) return deny;

        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        user.IsActive = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await Task.WhenAll(
            TryInvalidateListCache(),
            cache.RemoveByPrefixAsync(StatsPrefix).ContinueWith(_ => { }),
            TryRemoveCache(DetailKey(id)));
        return Ok(new { user.Id, user.IsActive, Message = "ปิดการใช้งานบัญชีเรียบร้อยแล้ว" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/admin/users/{id}/reset-password
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, CancellationToken ct = default)
    {
        var (_, deny) = await GuardMutationAsync(id, ct);
        if (deny is not null) return deny;

        var provider = await db.UserLoginProviders
            .FirstOrDefaultAsync(lp => lp.UserId == id && lp.Provider == "local", ct);

        if (provider is null)
            return BadRequest(new { Error = "ผู้ใช้นี้ไม่ได้ใช้ระบบรหัสผ่าน" });

        var tempPassword = GenerateTempPassword();
        provider.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

        var user = await db.Users.FindAsync([id], ct);
        if (user != null) user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await TryRemoveCache(DetailKey(id));
        return Ok(new { TempPassword = tempPassword, Message = "รีเซ็ตรหัสผ่านเรียบร้อยแล้ว กรุณาแจ้งรหัสชั่วคราวแก่ผู้ใช้" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/v1/admin/users/{id}/contacts
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPut("{id:int}/contacts")]
    public async Task<IActionResult> UpdateContacts(
        int id,
        [FromBody] UpdateContactsRequest request,
        CancellationToken ct = default)
    {
        var (_, deny) = await GuardMutationAsync(id, ct);
        if (deny is not null) return deny;

        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        var contactPrefs = string.IsNullOrEmpty(user.PreferencesJson)
            ? new Dictionary<string, string?>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(user.PreferencesJson) ?? new();

        contactPrefs["lineId"] = request.LineId;
        contactPrefs["facebookUrl"] = request.FacebookUrl;
        contactPrefs["telegramUsername"] = request.TelegramUsername;

        user.PreferencesJson = System.Text.Json.JsonSerializer.Serialize(contactPrefs);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        // Contacts only affects detail view, not list
        await TryRemoveCache(DetailKey(id));
        return Ok(new { user.Id, Message = "อัปเดตข้อมูลติดต่อเรียบร้อยแล้ว" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/admin/users/sync-from-personnel
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("sync-from-personnel")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")] // school_admin cannot sync
    public async Task<IActionResult> SyncFromPersonnel(CancellationToken ct = default)
    {
        // ── 1. Load all unlinked personnel (with needed navigations) ──────────
        var unlinked = await db.Personnel
            .AsNoTracking()
            .Include(p => p.TitlePrefix)
            .Include(p => p.PositionType)
            .Include(p => p.SchoolAssignments.Where(sa => sa.IsPrimary))
            .Where(p => p.UserId == null)
            .ToListAsync(ct);

        // NOTE: do NOT early-return here even if unlinked is empty.
        // Step 6 (reconcile) must still run to heal Personnel.UserId gaps from older code runs.

        // ── 2. Pre-load all existing usernames to avoid per-row roundtrips ────
        var existingUsernames = await db.Users
            .Select(u => u.Username)
            .ToHashSetAsync(ct);

        var roles = await db.Roles.ToDictionaryAsync(r => r.Code, r => r.Id, ct);

        // ── 3. Build entity graph in-memory — no DB calls inside the loop ─────
        var newUsers       = new List<User>();
        var personnelLinks = new List<(int PersonnelId, User UserRef)>();
        var providers      = new List<UserLoginProvider>();
        var userRoles      = new List<UserRole>();

        int skipped = 0;
        var errors  = new List<string>();

        foreach (var p in unlinked)
        {
            var username = !string.IsNullOrWhiteSpace(p.IdCard)
                ? p.IdCard.Trim()
                : p.PersonnelCode;

            if (existingUsernames.Contains(username))
            {
                skipped++;
                continue;
            }
            existingUsernames.Add(username); // prevent dups within the batch

            var email = !string.IsNullOrWhiteSpace(p.Email)
                ? p.Email.Trim().ToLower()
                : $"{p.PersonnelCode.ToLower()}@ssk3.go.th";

            var displayName = $"{p.TitlePrefix?.NameTh ?? ""}{p.FirstName} {p.LastName}".Trim();

            var user = new User
            {
                Username        = username,
                Email           = email,
                DisplayName     = displayName,
                IsActive        = true,
                CreatedAt       = DateTimeOffset.UtcNow,
                PersonnelContext = "school",
                PersonnelRefId  = p.Id,
            };

            newUsers.Add(user);
            personnelLinks.Add((p.Id, user));

            providers.Add(new UserLoginProvider
            {
                User      = user,  // EF resolves FK after SaveChanges
                Provider  = "local",
                ProviderKey = username,
                LinkedAt  = DateTimeOffset.UtcNow,
            });

            // ผู้บริหาร (ผอ./รองผอ.) → school_admin, ทุกอย่างอื่น → teacher
            var roleCode = IsSchoolAdminPersonnel(p.PositionType?.Category, p.PersonnelType)
                ? "school_admin" : "teacher";
            if (roles.TryGetValue(roleCode, out var roleId))
            {
                var primarySchoolId = p.SchoolAssignments.FirstOrDefault()?.SchoolId;
                userRoles.Add(new UserRole
                {
                    User      = user,
                    RoleId    = roleId,
                    ScopeType = primarySchoolId.HasValue ? "School" : null,
                    ScopeId   = primarySchoolId,
                    AssignedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        // ── 4. Single bulk insert ──────────────────────────────────────────────
        if (newUsers.Count > 0)
        {
            db.Users.AddRange(newUsers);
            db.UserLoginProviders.AddRange(providers);
            db.UserRoles.AddRange(userRoles);
            await db.SaveChangesAsync(ct); // ONE roundtrip for everything

            // ── 5. Bulk-update Personnel.UserId for newly created users ────────
            var personnelIdToUserId = personnelLinks
                .ToDictionary(x => x.PersonnelId, x => x.UserRef.Id);

            var personnelToUpdate = await db.Personnel
                .Where(p => personnelIdToUserId.Keys.Contains(p.Id))
                .ToListAsync(ct);

            foreach (var pr in personnelToUpdate)
                pr.UserId = personnelIdToUserId[pr.Id];

            await db.SaveChangesAsync(ct); // second roundtrip: update links
        }

        // ── 6. Reconcile orphaned links — Personnel.UserId is null but a matching User exists.
        //    Run even when newUsers.Count = 0 so re-running sync always heals gaps left by older code.
        //    Match priority: PersonnelRefId → Email → IdCard (username) → PersonnelCode (username)

        // 6a: match via PersonnelRefId (users created by newer sync code)
        var byRefId = await db.Personnel
            .Where(p => p.UserId == null)
            .Join(db.Users.Where(u => u.PersonnelRefId != null),
                  p => p.Id,
                  u => u.PersonnelRefId!.Value,
                  (p, u) => new { Personnel = p, UserId = u.Id })
            .ToListAsync(ct);

        // 6b: match via Email (Google / ThaiID users — their email matches Personnel.Email)
        var byEmail = await db.Personnel
            .Where(p => p.UserId == null && p.Email != null)
            .Join(db.Users,
                  p => p.Email!.ToLower(),
                  u => u.Email.ToLower(),
                  (p, u) => new { Personnel = p, UserId = u.Id })
            .ToListAsync(ct);

        // 6c: match via Username = IdCard (sync-created users — IdCard used as username)
        var byIdCard = await db.Personnel
            .Where(p => p.UserId == null && p.IdCard != null)
            .Join(db.Users,
                  p => p.IdCard,
                  u => u.Username,
                  (p, u) => new { Personnel = p, UserId = u.Id })
            .ToListAsync(ct);

        // 6d: match via Username = PersonnelCode (fallback)
        var byCode = await db.Personnel
            .Where(p => p.UserId == null && p.PersonnelCode != null)
            .Join(db.Users,
                  p => p.PersonnelCode,
                  u => u.Username,
                  (p, u) => new { Personnel = p, UserId = u.Id })
            .ToListAsync(ct);

        // Build repair map: PersonnelId → UserId (dedup by first match)
        var repairMap = byRefId
            .Concat(byEmail)
            .Concat(byIdCard)
            .Concat(byCode)
            .GroupBy(x => x.Personnel.Id)
            .Select(g => g.First())
            .ToDictionary(x => x.Personnel.Id, x => x.UserId);

        // Use AsTracking() to get EF-tracked entities — global NoTracking doesn't prevent this
        if (repairMap.Count > 0)
        {
            var personnelToFix = await db.Personnel
                .AsTracking()
                .Where(p => repairMap.Keys.Contains(p.Id))
                .ToListAsync(ct);

            foreach (var p in personnelToFix)
                p.UserId = repairMap[p.Id];

            await db.SaveChangesAsync(ct);
        }

        var repairedCount = repairMap.Count;

        // Backfill PersonnelRefId on Users that are missing it (same tracking fix)
        var refIdPairs = await db.Personnel
            .Where(p => p.UserId != null)
            .Join(db.Users.Where(u => u.PersonnelRefId == null),
                  p => p.UserId!.Value,
                  u => u.Id,
                  (p, u) => new { UserId = u.Id, PersonnelId = p.Id })
            .ToListAsync(ct);

        if (refIdPairs.Count > 0)
        {
            var userIds = refIdPairs.Select(x => x.UserId).ToList();
            var usersToFix = await db.Users
                .AsTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync(ct);

            var refIdMap = refIdPairs.ToDictionary(x => x.UserId, x => x.PersonnelId);
            foreach (var u in usersToFix)
                u.PersonnelRefId = refIdMap[u.Id];

            await db.SaveChangesAsync(ct);
        }

        // ── 7. Assign missing roles — users linked to Personnel but have no UserRole yet ──
        var usersWithoutRole = await db.Users
            .Where(u => u.PersonnelRefId != null && !u.UserRoles.Any())
            .Include(u => u.Personnel)
                .ThenInclude(p => p!.PositionType)
            .Include(u => u.Personnel)
                .ThenInclude(p => p!.SchoolAssignments.Where(sa => sa.IsPrimary))
            .ToListAsync(ct);

        var rolesAssignedCount = 0;
        if (usersWithoutRole.Count > 0)
        {
            var hasTeacherRole    = roles.TryGetValue("teacher", out var teacherRoleId);
            var hasSchoolAdminRole = roles.TryGetValue("school_admin", out var schoolAdminRoleId);

            var roleAssignments = new List<UserRole>();
            foreach (var u in usersWithoutRole)
            {
                var isAdmin = IsSchoolAdminPersonnel(
                    u.Personnel?.PositionType?.Category,
                    u.Personnel?.PersonnelType);
                if (isAdmin && !hasSchoolAdminRole) continue;
                if (!isAdmin && !hasTeacherRole) continue;
                var roleId = isAdmin ? schoolAdminRoleId : teacherRoleId;

                var primarySchoolId = u.Personnel?.SchoolAssignments.FirstOrDefault()?.SchoolId;
                roleAssignments.Add(new UserRole
                {
                    UserId     = u.Id,
                    RoleId     = roleId,
                    ScopeType  = primarySchoolId.HasValue ? "School" : null,
                    ScopeId    = primarySchoolId,
                    AssignedAt = DateTimeOffset.UtcNow,
                });
            }

            if (roleAssignments.Count > 0)
            {
                db.UserRoles.AddRange(roleAssignments);
                await db.SaveChangesAsync(ct);
                rolesAssignedCount = roleAssignments.Count;
            }
        }

        // Sync affects everything — clear all user caches
        await Task.WhenAll(
            TryInvalidateListCache(),
            cache.RemoveByPrefixAsync(StatsPrefix).ContinueWith(_ => { }));

        return Ok(new
        {
            Created       = newUsers.Count,
            Skipped       = skipped,
            Errors        = errors,
            Repaired      = repairedCount,
            RolesAssigned = rolesAssignedCount,
        });
    }

    /// <summary>
    /// ผู้บริหารสถานศึกษา = ผอ. และ รองผอ. ทุกคนใน Category "ผู้บริหาร"
    /// ทุกตำแหน่งอื่น (ครู, ธุรการ, อัตราจ้าง, ครูพี่เลี้ยง ฯลฯ) → teacher
    /// </summary>
    private static bool IsSchoolAdminPersonnel(string? positionCategory, string? personnelType)
        => positionCategory == "ผู้บริหาร" || personnelType == "Director";

    private static string GenerateTempPassword()
    {
        const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[8];
        rng.GetBytes(bytes);
        return "P@" + new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }
}

public record UpdateUserRequest(
    string? DisplayName,
    string? Email,
    string? Phone,
    bool? IsActive);

public record UpdateContactsRequest(
    string? LineId,
    string? FacebookUrl,
    string? TelegramUsername);
