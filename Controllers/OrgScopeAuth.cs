using System.Security.Claims;
using Gateway.Services;

namespace Gateway.Controllers;

/// <summary>
/// Plan #46 T5 — Scope-aware authz helper for the org-structure endpoints.
/// SchoolAdmin → only their own school_code claim.
/// SuperAdmin / AreaAdmin → pass through (refined later via HCD Phase B).
///
/// Feedback id=57 — "ให้ ผอ. / รอง ผอ. / หัวหน้างานที่ได้รับมอบหมาย แก้ได้".
/// Deputy directors and work-group heads log in as Teacher, so the role check
/// alone locked them out of a chart it is their job to maintain. The async
/// overload adds the HCD capability path, mirroring what SchoolWriteScope does
/// for school.profile.manage (Plan #111 U7).
/// </summary>
internal static class OrgScopeAuth
{
    public const string ManageCapability = "school.org_structure.manage";

    public static bool CanAccessSchool(ClaimsPrincipal user, string schoolCode)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value
            ?? user.FindFirst("role")?.Value;
        if (role == "SuperAdmin" || role == "AreaAdmin") return true;
        if (role == "SchoolAdmin")
        {
            var claim = user.FindFirst("school_code")?.Value
                ?? user.FindFirst("school_id")?.Value;
            return !string.IsNullOrEmpty(claim) && claim == schoolCode;
        }
        return false;
    }

    /// <summary>
    /// Role check first (cheap, claim-only), then the capability grant. Numeric
    /// school scope ids are the SchoolCode, same convention as SchoolWriteScope.
    /// </summary>
    public static async Task<bool> CanAccessSchoolAsync(
        ClaimsPrincipal user,
        string schoolCode,
        ICapabilityService capabilities,
        CancellationToken ct = default)
    {
        if (CanAccessSchool(user, schoolCode)) return true;

        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!int.TryParse(userIdStr, out var userId)) return false;
        if (!int.TryParse(schoolCode, out var schoolScopeNumeric)) return false;

        var capV = long.TryParse(user.FindFirstValue("cap_v"), out var v) ? v : 0L;
        return await capabilities.HasCapabilityAsync(
            userId, capV, ManageCapability, "school", schoolScopeNumeric, ct);
    }
}
