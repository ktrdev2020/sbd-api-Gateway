using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Gateway.Services;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;
using SBD.Messaging.Commands;
using SBD.Messaging.Events;

namespace Gateway.Controllers;

/// <summary>
/// Admin endpoints for personnel management.
/// SuperAdmin  — sees all personnel across all areas.
/// AreaAdmin   — sees/manages all personnel in their area's schools.
/// SchoolAdmin — sees own school personnel; can mutate only if area policy allows.
///
/// Permission key: "personnel.manage_school_personnel" (opt-out — default = allowed).
/// </summary>
[ApiController]
[Route("api/v1/admin/personnel")]
[Authorize(Roles = "super_admin,area_admin,school_admin,SuperAdmin,AreaAdmin,SchoolAdmin")]
public class PersonnelAdminController(
    SbdDbContext db,
    ICacheService cache,
    IPublishEndpoint bus) : ControllerBase
{
    // ─── Cache key helpers ────────────────────────────────────────────────────
    private static readonly TimeSpan ListTtl   = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StatsTtl  = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(2);

    private const string ListPrefix  = "admin:personnel:list:";
    private const string StatsPrefix = "admin:personnel:stats:";
    private static string DetailKey(int id)        => $"admin:personnel:detail:{id}";
    private static string ScopedStatsKey(string t) => $"{StatsPrefix}{t}";

    private static string ListCacheKey(
        string scopeTag, string? search, string? type, string? status,
        string? subjectArea, int? schoolId, int page, int pageSize)
    {
        var raw  = $"{scopeTag}|{search}|{type}|{status}|{subjectArea}|{schoolId}|{page}|{pageSize}";
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
        return $"{ListPrefix}{hash}";
    }

    // ─── Caller scope ─────────────────────────────────────────────────────────
    private record CallerScope(
        string Role,
        int? AreaId,
        int? SchoolId,
        IReadOnlyList<int> AllowedSchoolIds,
        string CacheTag,
        bool CanMutate = true);

    private async Task<CallerScope?> GetCallerScopeAsync(bool readOnly = false, CancellationToken ct = default)
    {
        if (User.IsInRole("super_admin") || User.IsInRole("SuperAdmin"))
            return new CallerScope("super_admin", null, null, Array.Empty<int>(), "global");

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(userIdStr, out var userId)) return null;

        // area_admin
        var areaRole = await db.UserRoles
            .Include(ur => ur.Role)
            .FirstOrDefaultAsync(ur =>
                ur.UserId == userId
                && ur.Role.Code == "area_admin"
                && ur.ScopeType == "Area"
                && ur.ScopeId.HasValue, ct);

        if (areaRole is not null)
        {
            var areaId   = areaRole.ScopeId!.Value;
            var schoolIds = await db.Schools
                .AsNoTracking()
                .Where(s => s.AreaId == areaId)
                .Select(s => s.Id)
                .ToListAsync(ct);
            return new CallerScope("area_admin", areaId, null, schoolIds, $"area:{areaId}");
        }

        // school_admin — 3-tier policy check for "personnel.manage_school_personnel"
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

            const string code = "personnel.manage_school_personnel";

            var canMutate = true;
            var schoolPolicy = await db.AreaPermissionPolicies
                .FirstOrDefaultAsync(p =>
                    p.AreaId == school.AreaId
                    && p.SchoolId == schoolId
                    && p.PermissionCode == code, ct);

            if (schoolPolicy is not null)
            {
                canMutate = schoolPolicy.AllowSchoolAdmin;
            }
            else
            {
                var areaPolicy = await db.AreaPermissionPolicies
                    .FirstOrDefaultAsync(p =>
                        p.AreaId == school.AreaId
                        && p.SchoolId == null
                        && p.PermissionCode == code, ct);
                if (areaPolicy is not null) canMutate = areaPolicy.AllowSchoolAdmin;
            }

            if (!canMutate && !readOnly) return null;
            return new CallerScope("school_admin", null, schoolId, new[] { schoolId }, $"school:{schoolId}", canMutate);
        }

        return null;
    }

    private static IQueryable<Personnel> ApplyScopeFilter(IQueryable<Personnel> q, CallerScope scope)
    {
        if (scope.Role == "super_admin") return q;
        if (scope.AllowedSchoolIds.Count == 0) return q.Where(_ => false);
        return q.Where(p =>
            p.SchoolAssignments.Any(sa =>
                sa.IsPrimary && scope.AllowedSchoolIds.Contains(sa.SchoolId)));
    }

    // ─── Cache helpers ────────────────────────────────────────────────────────
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
    private async Task TryInvalidateListAndStats(string cacheTag)
    {
        try
        {
            await cache.RemoveByPrefixAsync(ListPrefix);
            await cache.RemoveAsync(ScopedStatsKey(cacheTag));
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/admin/personnel
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetPersonnel(
        [FromQuery] string? search,
        [FromQuery] string? type,          // PersonnelType filter
        [FromQuery] string? status,        // AffiliationStatus filter
        [FromQuery] string? subjectArea,
        [FromQuery] int?    schoolId,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 30,
        CancellationToken   ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: true, ct);
        if (scope is null) return Forbid();

        if (scope.Role == "school_admin") schoolId = scope.SchoolId;

        var cacheKey = ListCacheKey(scope.CacheTag, search, type, status, subjectArea, schoolId, page, pageSize);
        var cached   = await TryGetCache<object>(cacheKey);
        if (cached is not null) return Ok(cached);

        var q = db.Personnel
            .AsNoTracking()
            .Include(p => p.TitlePrefix)
            .Include(p => p.PositionType)
            .Include(p => p.AcademicStandingType)
            .Include(p => p.SchoolAssignments)
                .ThenInclude(sa => sa.School)
            .Where(p => p.AffiliationStatus != "trashed")
            .AsQueryable();

        q = ApplyScopeFilter(q, scope);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p =>
                p.FirstName.Contains(search) ||
                p.LastName.Contains(search)  ||
                p.PersonnelCode.Contains(search) ||
                (p.IdCard != null && p.IdCard.Contains(search)));

        if (!string.IsNullOrWhiteSpace(type))
            q = q.Where(p => p.PersonnelType == type);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(p => p.AffiliationStatus == status);
        else
            q = q.Where(p => p.AffiliationStatus == "affiliated");

        if (!string.IsNullOrWhiteSpace(subjectArea))
            q = q.Where(p => p.SubjectArea == subjectArea);

        if (schoolId.HasValue)
            q = q.Where(p => p.SchoolAssignments.Any(sa => sa.SchoolId == schoolId.Value && sa.IsPrimary));

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(p => p.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.PersonnelCode,
                p.IdCard,
                p.PersonnelType,
                p.AffiliationStatus,
                p.SubjectArea,
                p.Specialty,
                TitlePrefix = p.TitlePrefix != null ? p.TitlePrefix.NameTh : null,
                p.FirstName,
                p.LastName,
                PositionType = p.PositionType != null ? p.PositionType.NameTh : null,
                AcademicStanding = p.AcademicStandingType != null ? p.AcademicStandingType.NameTh : null,
                PrimarySchool = p.SchoolAssignments
                    .Where(sa => sa.IsPrimary)
                    .Select(sa => new { sa.SchoolId, sa.School.NameTh, sa.SpecialRoleType })
                    .FirstOrDefault(),
                p.UserId,
                p.UpdatedAt,
            })
            .ToListAsync(ct);

        var result = new { total, page, pageSize, items };
        await TrySetCache(cacheKey, result, ListTtl);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/admin/personnel/stats
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: true, ct);
        if (scope is null) return Forbid();

        var cacheKey = ScopedStatsKey(scope.CacheTag);
        var cached   = await TryGetCache<object>(cacheKey);
        if (cached is not null) return Ok(cached);

        var q = db.Personnel.AsNoTracking()
            .Where(p => p.AffiliationStatus != "trashed");
        q = ApplyScopeFilter(q, scope);

        var byType   = await q.GroupBy(p => p.PersonnelType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byStatus = await q.GroupBy(p => p.AffiliationStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total      = byType.Sum(x => x.Count);
        var affiliated = byStatus.FirstOrDefault(x => x.Status == "affiliated")?.Count ?? 0;
        var unaffiliated = byStatus.FirstOrDefault(x => x.Status == "unaffiliated")?.Count ?? 0;

        // Per-school breakdown (for area/super views)
        List<object>? bySchool = null;
        if (scope.Role != "school_admin")
        {
            bySchool = (await db.Personnel.AsNoTracking()
                .Where(p => p.AffiliationStatus == "affiliated")
                .SelectMany(p => p.SchoolAssignments.Where(sa => sa.IsPrimary))
                .GroupBy(sa => new { sa.SchoolId, sa.School.NameTh })
                .Select(g => new { g.Key.SchoolId, g.Key.NameTh, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(ct))
                .Cast<object>()
                .ToList();
        }

        var result = new { total, affiliated, unaffiliated, byType, bySchool };
        await TrySetCache(cacheKey, result, StatsTtl);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/admin/personnel/trash
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("trash")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> GetTrash(
        [FromQuery] int? schoolId,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: true, ct);
        if (scope is null) return Forbid();

        var q = db.Personnel.AsNoTracking()
            .Include(p => p.TitlePrefix)
            .Include(p => p.SchoolAssignments).ThenInclude(sa => sa.School)
            .Where(p => p.AffiliationStatus == "trashed");

        q = ApplyScopeFilter(q, scope);

        if (schoolId.HasValue)
            q = q.Where(p => p.SchoolAssignments.Any(sa => sa.SchoolId == schoolId.Value));

        var items = await q
            .OrderByDescending(p => p.TrashedAt)
            .Select(p => new
            {
                p.Id,
                p.PersonnelCode,
                p.PersonnelType,
                TitlePrefix = p.TitlePrefix != null ? p.TitlePrefix.NameTh : null,
                p.FirstName,
                p.LastName,
                p.TrashedAt,
                p.TrashedByUserId,
                p.UserId,
                LastSchool = p.SchoolAssignments
                    .OrderByDescending(sa => sa.EndDate)
                    .Select(sa => new { sa.SchoolId, sa.School.NameTh })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Ok(new { total = items.Count, items });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/admin/personnel/manage-permission
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("manage-permission")]
    public async Task<IActionResult> GetManagePermission(CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: true, ct);
        if (scope?.Role is "super_admin" or "area_admin")
            return Ok(new { canManage = true, schoolId = scope.SchoolId });
        return Ok(new { canManage = scope?.CanMutate ?? false, schoolId = scope?.SchoolId });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/admin/personnel/school-role
    // Returns the SpecialRoleType of the requesting school_admin for approval routing.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("school-role")]
    public async Task<IActionResult> GetSchoolRole(CancellationToken ct = default)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(userIdStr, out var userId))
            return Ok(new { specialRoleType = "none", hasDeputy = false });

        var scope = await GetCallerScopeAsync(readOnly: true, ct);
        if (scope?.SchoolId is null)
            return Ok(new { specialRoleType = "none", hasDeputy = false });

        // Current user's special role
        var myAssignment = await db.PersonnelSchoolAssignments
            .AsNoTracking()
            .Include(sa => sa.Personnel)
            .Where(sa => sa.SchoolId == scope.SchoolId
                && sa.IsPrimary
                && sa.Personnel.UserId == userId)
            .Select(sa => sa.SpecialRoleType)
            .FirstOrDefaultAsync(ct);

        // Does this school have a deputy director?
        var hasDeputy = await db.PersonnelSchoolAssignments
            .AsNoTracking()
            .AnyAsync(sa =>
                sa.SchoolId == scope.SchoolId
                && sa.IsPrimary
                && (sa.SpecialRoleType == "deputy_director" || sa.SpecialRoleType == "acting_deputy"), ct);

        return Ok(new
        {
            specialRoleType = myAssignment ?? "none",
            hasDeputy,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/admin/personnel/{id}
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetPersonnelById(int id, CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: true, ct);
        if (scope is null) return Forbid();

        var cached = await TryGetCache<object>(DetailKey(id));
        if (cached is not null) return Ok(cached);

        var p = await db.Personnel
            .AsNoTracking()
            .Include(x => x.TitlePrefix)
            .Include(x => x.PositionType)
            .Include(x => x.AcademicStandingType)
            .Include(x => x.SchoolAssignments).ThenInclude(sa => sa.School)
            .Include(x => x.Educations)
            .Include(x => x.Certifications)
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (p is null) return NotFound();

        // Scope check
        if (scope.Role != "super_admin")
        {
            var schoolIds = p.SchoolAssignments.Where(sa => sa.IsPrimary).Select(sa => sa.SchoolId).ToList();
            if (!schoolIds.Any(s => scope.AllowedSchoolIds.Contains(s))) return Forbid();
        }

        var result = new
        {
            p.Id,
            p.PersonnelCode,
            p.IdCard,
            p.PersonnelType,
            p.AffiliationStatus,
            p.Gender,
            p.BirthDate,
            p.Phone,
            p.Email,
            p.LineId,
            p.Photo,
            p.SubjectArea,
            p.Specialty,
            TitlePrefix      = p.TitlePrefix,
            PositionType     = p.PositionType,
            AcademicStanding = p.AcademicStandingType,
            SchoolAssignments = p.SchoolAssignments.Select(sa => new
            {
                sa.Id,
                sa.SchoolId,
                sa.School.NameTh,
                sa.Position,
                sa.AcademicRank,
                sa.SalaryLevel,
                sa.IsPrimary,
                sa.StartDate,
                sa.EndDate,
                sa.SpecialRoleType,
            }),
            Educations    = p.Educations,
            Certifications = p.Certifications,
            User = p.User is null ? null : new
            {
                p.User.Id,
                p.User.Username,
                p.User.DisplayName,
                p.User.IsActive,
            },
            p.UpdatedAt,
        };

        await TrySetCache(DetailKey(id), result, DetailTtl);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/admin/personnel/search-existing
    // Pre-check before creating — returns any matching record by IdCard or code.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("search-existing")]
    public async Task<IActionResult> SearchExisting(
        [FromBody] PersonnelSearchExistingRequest req,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();

        Personnel? match = null;
        if (!string.IsNullOrWhiteSpace(req.IdCard))
            match = await db.Personnel.AsNoTracking()
                .Include(p => p.TitlePrefix)
                .Include(p => p.SchoolAssignments).ThenInclude(sa => sa.School)
                .FirstOrDefaultAsync(p => p.IdCard == req.IdCard, ct);

        if (match is null && !string.IsNullOrWhiteSpace(req.PersonnelCode))
            match = await db.Personnel.AsNoTracking()
                .Include(p => p.TitlePrefix)
                .Include(p => p.SchoolAssignments).ThenInclude(sa => sa.School)
                .FirstOrDefaultAsync(p => p.PersonnelCode == req.PersonnelCode, ct);

        if (match is null) return Ok(new { found = false });

        return Ok(new
        {
            found = true,
            personnel = new
            {
                match.Id,
                match.PersonnelCode,
                match.IdCard,
                match.PersonnelType,
                match.AffiliationStatus,
                TitlePrefix = match.TitlePrefix?.NameTh,
                match.FirstName,
                match.LastName,
                PrimarySchool = match.SchoolAssignments
                    .Where(sa => sa.IsPrimary)
                    .Select(sa => new { sa.SchoolId, sa.School.NameTh })
                    .FirstOrDefault(),
                match.UserId,
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/admin/personnel
    // Create personnel + provision User account via WorkerService
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreatePersonnel(
        [FromBody] PersonnelAdminCreateRequest req,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();

        // Enforce school context
        var targetSchoolId = scope.Role == "school_admin"
            ? scope.SchoolId!.Value
            : req.SchoolId ?? throw new ArgumentException("schoolId required");

        if (scope.Role != "super_admin" && !scope.AllowedSchoolIds.Contains(targetSchoolId))
            return Forbid();

        // Duplicate check
        if (!string.IsNullOrWhiteSpace(req.IdCard))
        {
            var dup = await db.Personnel.AnyAsync(p => p.IdCard == req.IdCard, ct);
            if (dup) return Conflict(new { error = "มีบุคลากรที่ใช้เลขบัตรประชาชนนี้อยู่แล้ว" });
        }

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var requestedBy);

        var personnel = new Personnel
        {
            PersonnelCode = req.PersonnelCode,
            TitlePrefixId = req.TitlePrefixId,
            FirstName     = req.FirstName,
            LastName      = req.LastName,
            IdCard        = req.IdCard,
            PersonnelType = req.PersonnelType,
            Gender        = req.Gender,
            BirthDate     = req.BirthDate,
            Phone         = req.Phone,
            Email         = req.Email,
            SubjectArea   = req.SubjectArea,
            Specialty     = req.Specialty,
            PositionTypeId        = req.PositionTypeId,
            AcademicStandingTypeId = req.AcademicStandingTypeId,
            AffiliationStatus = "affiliated",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = requestedBy,
        };

        db.Personnel.Add(personnel);
        await db.SaveChangesAsync(ct);

        // Create primary school assignment
        db.PersonnelSchoolAssignments.Add(new PersonnelSchoolAssignment
        {
            PersonnelId     = personnel.Id,
            SchoolId        = targetSchoolId,
            IsPrimary       = true,
            Position        = req.Position,
            AcademicRank    = req.AcademicRank,
            SalaryLevel     = req.SalaryLevel,
            SpecialRoleType = req.SpecialRoleType ?? "none",
            StartDate       = req.StartDate ?? DateOnly.FromDateTime(DateTime.Today),
        });
        await db.SaveChangesAsync(ct);

        // Send command to WorkerService to provision User account (BCrypt off hot path)
        if (!string.IsNullOrWhiteSpace(req.IdCard))
        {
            await bus.Publish(new ProvisionPersonnelUserCommand
            {
                PersonnelId       = personnel.Id,
                PersonnelCode     = personnel.PersonnelCode,
                IdCard            = req.IdCard,
                FirstName         = personnel.FirstName,
                LastName          = personnel.LastName,
                PersonnelType     = personnel.PersonnelType,
                SchoolId          = targetSchoolId,
                RequestedByUserId = requestedBy,
            }, ct);
        }

        await TryInvalidateListAndStats(scope.CacheTag);

        return CreatedAtAction(nameof(GetPersonnelById), new { id = personnel.Id },
            new { id = personnel.Id, personnelCode = personnel.PersonnelCode });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/v1/admin/personnel/{id}
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdatePersonnel(
        int id,
        [FromBody] PersonnelAdminUpdateRequest req,
        CancellationToken ct = default)
    {
        var (scope, deny) = await GuardMutationAsync(id, ct);
        if (deny is not null) return deny;

        var p = await db.Personnel.FindAsync([id], ct);
        if (p is null) return NotFound();

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var requestedBy);

        if (req.TitlePrefixId.HasValue) p.TitlePrefixId = req.TitlePrefixId;
        if (req.FirstName is not null)  p.FirstName  = req.FirstName;
        if (req.LastName is not null)   p.LastName   = req.LastName;
        if (req.Phone is not null)      p.Phone      = req.Phone;
        if (req.Email is not null)      p.Email      = req.Email;
        if (req.SubjectArea is not null) p.SubjectArea = req.SubjectArea;
        if (req.Specialty is not null)  p.Specialty  = req.Specialty;
        if (req.PositionTypeId.HasValue) p.PositionTypeId = req.PositionTypeId;
        if (req.AcademicStandingTypeId.HasValue) p.AcademicStandingTypeId = req.AcademicStandingTypeId;
        if (req.BirthDate.HasValue)     p.BirthDate  = req.BirthDate;
        p.UpdatedAt = DateTimeOffset.UtcNow;
        p.UpdatedBy = requestedBy;

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(DetailKey(id));
        await TryInvalidateListAndStats(scope!.CacheTag);

        return Ok(new { id = p.Id });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/admin/personnel/{id}/unaffiliate
    // Remove from school → status = unaffiliated, user deactivated
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("{id:int}/unaffiliate")]
    public async Task<IActionResult> Unaffiliate(int id, CancellationToken ct = default)
    {
        var (scope, deny) = await GuardMutationAsync(id, ct);
        if (deny is not null) return deny;

        var p = await db.Personnel
            .Include(x => x.SchoolAssignments)
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var requestedBy);

        var primaryAssignment = p.SchoolAssignments.FirstOrDefault(sa => sa.IsPrimary);
        var previousSchoolId  = primaryAssignment?.SchoolId ?? 0;

        if (primaryAssignment is not null)
        {
            primaryAssignment.EndDate  = DateOnly.FromDateTime(DateTime.Today);
            primaryAssignment.IsPrimary = false;
        }

        p.AffiliationStatus = "unaffiliated";
        p.UpdatedAt = DateTimeOffset.UtcNow;
        p.UpdatedBy = requestedBy;

        if (p.User is not null)
            p.User.IsActive = false;

        await db.SaveChangesAsync(ct);

        await bus.Publish(new PersonnelUnaffiliatedEvent
        {
            PersonnelId       = id,
            PreviousSchoolId  = previousSchoolId,
            UserId            = p.UserId,
            RequestedByUserId = requestedBy,
            OccurredAt        = DateTimeOffset.UtcNow,
        }, ct);

        await cache.RemoveAsync(DetailKey(id));
        await TryInvalidateListAndStats(scope!.CacheTag);
        return Ok(new { id, status = "unaffiliated" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/admin/personnel/{id}/assign-to-school
    // Reassign unaffiliated personnel to a new school
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("{id:int}/assign-to-school")]
    public async Task<IActionResult> AssignToSchool(
        int id,
        [FromBody] AssignToSchoolRequest req,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();
        if (scope.Role != "super_admin" && !scope.AllowedSchoolIds.Contains(req.SchoolId))
            return Forbid();

        var p = await db.Personnel
            .Include(x => x.SchoolAssignments)
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (p.AffiliationStatus == "trashed")
            return BadRequest(new { error = "ไม่สามารถย้ายบุคลากรที่อยู่ในถังขยะ" });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var requestedBy);

        db.PersonnelSchoolAssignments.Add(new PersonnelSchoolAssignment
        {
            PersonnelId     = id,
            SchoolId        = req.SchoolId,
            IsPrimary       = true,
            Position        = req.Position,
            AcademicRank    = req.AcademicRank,
            SalaryLevel     = req.SalaryLevel,
            SpecialRoleType = req.SpecialRoleType ?? "none",
            StartDate       = DateOnly.FromDateTime(DateTime.Today),
        });

        p.AffiliationStatus = "affiliated";
        p.UpdatedAt  = DateTimeOffset.UtcNow;
        p.UpdatedBy  = requestedBy;

        if (p.User is not null)
            p.User.IsActive = true;

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(DetailKey(id));
        await TryInvalidateListAndStats(scope.CacheTag);
        return Ok(new { id, status = "affiliated", schoolId = req.SchoolId });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/v1/admin/personnel/{id}
    // Soft-delete → trash
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> SoftDelete(int id, CancellationToken ct = default)
    {
        var (scope, deny) = await GuardMutationAsync(id, ct);
        if (deny is not null) return deny;

        var p = await db.Personnel
            .Include(x => x.User)
            .Include(x => x.SchoolAssignments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (p.AffiliationStatus == "trashed")
            return BadRequest(new { error = "บุคลากรนี้อยู่ในถังขยะแล้ว" });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var requestedBy);

        var school = p.SchoolAssignments.FirstOrDefault(sa => sa.IsPrimary);
        var areaId = school is not null
            ? (await db.Schools.AsNoTracking().Select(s => new { s.Id, s.AreaId })
                .FirstOrDefaultAsync(s => s.Id == school.SchoolId, ct))?.AreaId ?? 0
            : 0;

        p.AffiliationStatus = "trashed";
        p.TrashedAt         = DateTimeOffset.UtcNow;
        p.TrashedByUserId   = requestedBy;
        p.UpdatedAt         = DateTimeOffset.UtcNow;
        p.UpdatedBy         = requestedBy;

        if (p.User is not null) p.User.IsActive = false;

        await db.SaveChangesAsync(ct);

        await bus.Publish(new PersonnelTrashedEvent
        {
            PersonnelId    = id,
            AreaId         = areaId,
            UserId         = p.UserId,
            TrashedByUserId = requestedBy,
            TrashedAt      = DateTimeOffset.UtcNow,
        }, ct);

        await cache.RemoveAsync(DetailKey(id));
        await TryInvalidateListAndStats(scope!.CacheTag);
        return Ok(new { id, status = "trashed" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/v1/admin/personnel/{id}/permanent
    // Permanently delete from DB (area_admin / super_admin only)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("{id:int}/permanent")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> PermanentDelete(int id, CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();

        var p = await db.Personnel
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (p.AffiliationStatus != "trashed")
            return BadRequest(new { error = "ต้องย้ายไปถังขยะก่อนจึงจะลบถาวรได้" });

        // Scope check for area_admin
        if (scope.Role == "area_admin")
        {
            var isInScope = await db.PersonnelSchoolAssignments
                .AnyAsync(sa => sa.PersonnelId == id
                    && scope.AllowedSchoolIds.Contains(sa.SchoolId), ct);
            if (!isInScope) return Forbid();
        }

        db.Personnel.Remove(p);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(DetailKey(id));
        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/admin/personnel/{id}/restore
    // Restore from trash → unaffiliated
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("{id:int}/restore")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> Restore(int id, CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();

        var p = await db.Personnel.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (p.AffiliationStatus != "trashed")
            return BadRequest(new { error = "บุคลากรนี้ไม่ได้อยู่ในถังขยะ" });

        p.AffiliationStatus = "unaffiliated";
        p.TrashedAt         = null;
        p.TrashedByUserId   = null;
        p.UpdatedAt         = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(DetailKey(id));
        return Ok(new { id, status = "unaffiliated" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Approval Cycle endpoints
    // ─────────────────────────────────────────────────────────────────────────

    // GET /api/v1/admin/personnel/cycles
    [HttpGet("cycles")]
    public async Task<IActionResult> GetCycles(
        [FromQuery] int? schoolId,
        [FromQuery] int? academicYearId,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: true, ct);
        if (scope is null) return Forbid();

        if (scope.Role == "school_admin") schoolId = scope.SchoolId;

        var q = db.PersonnelApprovalCycles
            .AsNoTracking()
            .Include(c => c.School)
            .Include(c => c.AcademicYear)
            .AsQueryable();

        if (scope.Role != "super_admin")
            q = q.Where(c => scope.AllowedSchoolIds.Contains(c.SchoolId));

        if (schoolId.HasValue) q = q.Where(c => c.SchoolId == schoolId.Value);
        if (academicYearId.HasValue) q = q.Where(c => c.AcademicYearId == academicYearId.Value);

        var cycles = await q
            .OrderByDescending(c => c.AcademicYear.Year)
            .Select(c => new
            {
                c.Id,
                c.SchoolId,
                SchoolName = c.School.NameTh,
                c.AcademicYearId,
                AcademicYear = c.AcademicYear.Year,
                c.Status,
                c.OpenedAt,
                c.StaffSubmittedAt,
                c.DeputyReviewedAt,
                c.PrincipalApprovedAt,
                c.AreaAcceptedAt,
                c.ReopenedAt,
                c.ReopenNote,
            })
            .ToListAsync(ct);

        return Ok(cycles);
    }

    // POST /api/v1/admin/personnel/cycles/submit
    [HttpPost("cycles/submit")]
    public async Task<IActionResult> SubmitCycle(
        [FromBody] CycleActionRequest req,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();

        var cycle = await GetOrCreateCycleAsync(req.SchoolId, req.AcademicYearId, ct);
        if (cycle.Status != "open" && cycle.Status != "reopened")
            return BadRequest(new { error = $"ไม่สามารถส่งได้ สถานะปัจจุบัน: {cycle.Status}" });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var userId);

        var prev = cycle.Status;

        // Determine if this school has a deputy director → go to deputy_review first
        var hasDeputy = await db.PersonnelSchoolAssignments
            .AnyAsync(sa =>
                sa.SchoolId == req.SchoolId
                && sa.IsPrimary
                && (sa.SpecialRoleType == "deputy_director" || sa.SpecialRoleType == "acting_deputy"), ct);

        cycle.Status              = hasDeputy ? "deputy_review" : "principal_review";
        cycle.StaffSubmittedAt    = DateTimeOffset.UtcNow;
        cycle.StaffSubmittedByUserId = userId;

        await db.SaveChangesAsync(ct);
        await PublishApprovalAdvanced(cycle, prev, userId, null, ct);
        return Ok(new { cycleId = cycle.Id, status = cycle.Status });
    }

    // POST /api/v1/admin/personnel/cycles/{id}/deputy-approve
    [HttpPost("cycles/{id:int}/deputy-approve")]
    public async Task<IActionResult> DeputyApprove(int id, CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();

        var cycle = await db.PersonnelApprovalCycles.FindAsync([id], ct);
        if (cycle is null) return NotFound();
        if (cycle.Status != "deputy_review")
            return BadRequest(new { error = $"ไม่สามารถดำเนินการได้ สถานะ: {cycle.Status}" });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var userId);

        var prev = cycle.Status;
        cycle.Status               = "principal_review";
        cycle.DeputyReviewedAt     = DateTimeOffset.UtcNow;
        cycle.DeputyReviewedByUserId = userId;
        await db.SaveChangesAsync(ct);
        await PublishApprovalAdvanced(cycle, prev, userId, null, ct);
        return Ok(new { cycleId = id, status = cycle.Status });
    }

    // POST /api/v1/admin/personnel/cycles/{id}/principal-approve
    [HttpPost("cycles/{id:int}/principal-approve")]
    public async Task<IActionResult> PrincipalApprove(int id, CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();

        var cycle = await db.PersonnelApprovalCycles.FindAsync([id], ct);
        if (cycle is null) return NotFound();
        if (cycle.Status != "principal_review")
            return BadRequest(new { error = $"ไม่สามารถอนุมัติได้ สถานะ: {cycle.Status}" });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var userId);

        var prev = cycle.Status;
        cycle.Status                   = "locked";
        cycle.PrincipalApprovedAt      = DateTimeOffset.UtcNow;
        cycle.PrincipalApprovedByUserId = userId;
        await db.SaveChangesAsync(ct);
        await PublishApprovalAdvanced(cycle, prev, userId, null, ct);
        return Ok(new { cycleId = id, status = cycle.Status });
    }

    // POST /api/v1/admin/personnel/cycles/{id}/area-accept
    [HttpPost("cycles/{id:int}/area-accept")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> AreaAccept(int id, CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();

        var cycle = await db.PersonnelApprovalCycles.FindAsync([id], ct);
        if (cycle is null) return NotFound();
        if (cycle.Status != "locked")
            return BadRequest(new { error = $"ต้องรอ ผอ อนุมัติก่อน สถานะ: {cycle.Status}" });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var userId);

        var prev = cycle.Status;
        cycle.Status             = "area_accepted";
        cycle.AreaAcceptedAt     = DateTimeOffset.UtcNow;
        cycle.AreaAcceptedByUserId = userId;
        await db.SaveChangesAsync(ct);
        await PublishApprovalAdvanced(cycle, prev, userId, null, ct);
        return Ok(new { cycleId = id, status = cycle.Status });
    }

    // POST /api/v1/admin/personnel/cycles/{id}/reopen
    [HttpPost("cycles/{id:int}/reopen")]
    [Authorize(Roles = "super_admin,area_admin,SuperAdmin,AreaAdmin")]
    public async Task<IActionResult> Reopen(
        int id,
        [FromBody] ReopenRequest req,
        CancellationToken ct = default)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return Forbid();

        var cycle = await db.PersonnelApprovalCycles.FindAsync([id], ct);
        if (cycle is null) return NotFound();

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdStr, out var userId);

        var prev = cycle.Status;
        cycle.Status         = "reopened";
        cycle.ReopenedAt     = DateTimeOffset.UtcNow;
        cycle.ReopenedByUserId = userId;
        cycle.ReopenNote     = req.Note;
        await db.SaveChangesAsync(ct);
        await PublishApprovalAdvanced(cycle, prev, userId, req.Note, ct);
        return Ok(new { cycleId = id, status = "reopened" });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<PersonnelApprovalCycle> GetOrCreateCycleAsync(
        int schoolId, int academicYearId, CancellationToken ct)
    {
        var cycle = await db.PersonnelApprovalCycles
            .FirstOrDefaultAsync(c =>
                c.SchoolId == schoolId && c.AcademicYearId == academicYearId, ct);

        if (cycle is null)
        {
            cycle = new PersonnelApprovalCycle
            {
                SchoolId       = schoolId,
                AcademicYearId = academicYearId,
                Status         = "open",
                OpenedAt       = DateTimeOffset.UtcNow,
            };
            db.PersonnelApprovalCycles.Add(cycle);
            await db.SaveChangesAsync(ct);
        }

        return cycle;
    }

    private async Task PublishApprovalAdvanced(
        PersonnelApprovalCycle cycle,
        string previousStatus,
        int advancedByUserId,
        string? note,
        CancellationToken ct)
    {
        await bus.Publish(new PersonnelApprovalAdvancedEvent
        {
            CycleId          = cycle.Id,
            SchoolId         = cycle.SchoolId,
            AcademicYearId   = cycle.AcademicYearId,
            NewStatus        = cycle.Status,
            PreviousStatus   = previousStatus,
            AdvancedByUserId = advancedByUserId,
            Note             = note,
            OccurredAt       = DateTimeOffset.UtcNow,
        }, ct);
    }

    private async Task<(CallerScope? scope, IActionResult? deny)> GuardMutationAsync(
        int personnelId, CancellationToken ct)
    {
        var scope = await GetCallerScopeAsync(readOnly: false, ct);
        if (scope is null) return (null, Forbid());

        if (scope.Role != "super_admin")
        {
            var schoolIds = await db.PersonnelSchoolAssignments
                .AsNoTracking()
                .Where(sa => sa.PersonnelId == personnelId && sa.IsPrimary)
                .Select(sa => sa.SchoolId)
                .ToListAsync(ct);

            if (!schoolIds.Any(s => scope.AllowedSchoolIds.Contains(s)))
                return (null, Forbid());
        }

        return (scope, null);
    }
}

// ─── Request / Response DTOs ─────────────────────────────────────────────────

public record PersonnelSearchExistingRequest(string? IdCard, string? PersonnelCode);

public record PersonnelAdminCreateRequest(
    string     PersonnelCode,
    int?       TitlePrefixId,
    string     FirstName,
    string     LastName,
    string?    IdCard,
    string     PersonnelType,
    char       Gender,
    DateOnly?  BirthDate,
    string?    Phone,
    string?    Email,
    string?    SubjectArea,
    string?    Specialty,
    int?       PositionTypeId,
    int?       AcademicStandingTypeId,
    int?       SchoolId,          // required for area_admin / super_admin
    string?    Position,
    string?    AcademicRank,
    string?    SalaryLevel,
    string?    SpecialRoleType,   // none | acting_director | deputy_director
    DateOnly?  StartDate);

public record PersonnelAdminUpdateRequest(
    int?       TitlePrefixId,
    string?    FirstName,
    string?    LastName,
    string?    Phone,
    string?    Email,
    string?    SubjectArea,
    string?    Specialty,
    int?       PositionTypeId,
    int?       AcademicStandingTypeId,
    DateOnly?  BirthDate);

public record AssignToSchoolRequest(
    int       SchoolId,
    string?   Position,
    string?   AcademicRank,
    string?   SalaryLevel,
    string?   SpecialRoleType);

public record CycleActionRequest(int SchoolId, int AcademicYearId);
public record ReopenRequest(string? Note);
