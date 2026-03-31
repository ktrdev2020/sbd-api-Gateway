using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/user")]
[Authorize]
public class UserProfileController : ControllerBase
{
    private readonly SbdDbContext _context;

    public UserProfileController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get the current user's profile including areaId and schoolId from role scopes.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> GetMe()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { message = "Invalid token" });

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            return NotFound(new { message = "User not found" });

        var roles = user.UserRoles.Select(ur => ur.Role.Code).ToList();
        var activeRole = roles.FirstOrDefault() ?? "Student";

        var areaRole = user.UserRoles.FirstOrDefault(ur =>
            ur.ScopeType == "Area" && ur.ScopeId.HasValue);
        var schoolRole = user.UserRoles.FirstOrDefault(ur =>
            ur.ScopeType == "School" && ur.ScopeId.HasValue);

        return Ok(new UserProfileDto(
            Id: user.Id.ToString(),
            Username: user.Username,
            Email: user.Email,
            DisplayName: user.DisplayName,
            Roles: roles,
            ActiveRole: activeRole,
            AreaId: areaRole?.ScopeId?.ToString(),
            SchoolId: schoolRole?.ScopeId?.ToString(),
            Provider: user.LoginProviders.FirstOrDefault()?.ProviderName ?? "local"
        ));
    }
}

public record UserProfileDto(
    string Id,
    string Username,
    string Email,
    string DisplayName,
    List<string> Roles,
    string ActiveRole,
    string? AreaId,
    string? SchoolId,
    string Provider
);
