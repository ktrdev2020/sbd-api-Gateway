using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Services;

/// <summary>
/// Plan #111 Phase S — school-ownership guard for school-profile write endpoints.
///
/// Before this existed, <c>SchoolController.UpdateProfileExtended</c> and the
/// identity/stats/community controllers were plain <c>[Authorize]</c>: any
/// authenticated user (2,400+ teachers) could PUT changes to ANY school by
/// putting a different schoolCode in the URL. The frontend never offers that,
/// but the API accepted it.
///
/// Rules:
///   • SuperAdmin           → any school
///   • AreaAdmin            → schools inside their own area
///   • SchoolAdmin / others → only the school they are currently posted to
///
/// Resolution of "their school" mirrors <c>UserMenuController.GetUserSchoolCode</c>:
/// the UserRole School scope wins, otherwise the current primary posting.
/// </summary>
public interface ISchoolWriteScope
{
    Task<bool> CanWriteAsync(ClaimsPrincipal user, string schoolCode, CancellationToken ct = default);
}

public class SchoolWriteScope(SbdDbContext db) : ISchoolWriteScope
{
    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string schoolCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(schoolCode)) return false;

        if (user.IsInRole("super_admin") || user.IsInRole("SuperAdmin")) return true;

        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!int.TryParse(userIdStr, out var userId)) return false;

        // AreaAdmin — the target school must belong to the area they administer.
        var areaScopeId = await db.Set<UserRole>()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId
                         && ur.ScopeType == "Area"
                         && ur.ScopeId.HasValue
                         && ur.RevokedAt == null)
            .Select(ur => ur.ScopeId)
            .FirstOrDefaultAsync(ct);
        if (areaScopeId.HasValue)
        {
            var inArea = await db.Schools
                .AsNoTracking()
                .AnyAsync(s => s.SchoolCode == schoolCode && s.AreaId == areaScopeId.Value, ct);
            if (inArea) return true;
        }

        // School-scoped role wins over the personnel posting.
        var schoolScopeId = await db.Set<UserRole>()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId
                         && ur.ScopeType == "School"
                         && ur.ScopeId.HasValue
                         && ur.RevokedAt == null)
            .Select(ur => ur.ScopeId)
            .FirstOrDefaultAsync(ct);
        if (schoolScopeId.HasValue)
            return schoolScopeId.Value.ToString() == schoolCode;

        // Fallback — current primary posting (see personnel-school-assignment
        // invariant: IsPrimary AND not ended).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.Personnel != null)
            .SelectMany(u => u.Personnel!.SchoolAssignments)
            .AnyAsync(sa => sa.SchoolCode == schoolCode
                            && sa.IsPrimary
                            && (sa.EndDate == null || sa.EndDate >= today), ct);
    }
}
