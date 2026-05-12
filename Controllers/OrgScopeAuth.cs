using System.Security.Claims;

namespace Gateway.Controllers;

/// <summary>
/// Plan #46 T5 — Scope-aware authz helper for the org-structure endpoints.
/// SchoolAdmin → only their own school_code claim.
/// SuperAdmin / AreaAdmin → pass through (refined later via HCD Phase B).
/// </summary>
internal static class OrgScopeAuth
{
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
}
