using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Gateway.Filters;

/// <summary>
/// Requires the caller's JWT to have been issued within the last 15 minutes.
/// Apply to endpoints that operate on dangerous / irreversible capabilities
/// (e.g. Break-Glass activation, bulk revoke).
///
/// When the token is too old the action is short-circuited with:
///   HTTP 401 { "error": "step_up_required", "message": "..." }
///
/// The frontend intercepts this response and prompts the user to re-authenticate
/// before retrying the original request.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireStepUpAttribute : Attribute, IActionFilter
{
    /// Maximum age of the JWT (in minutes) allowed for step-up-protected endpoints.
    private const int MaxTokenAgeMinutes = 15;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var issuedAtClaim = context.HttpContext.User.FindFirstValue("iat");

        if (!long.TryParse(issuedAtClaim, out var iatUnix))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "step_up_required",
                message = "ไม่พบข้อมูลเวลาออก token — กรุณาเข้าสู่ระบบใหม่",
            });
            return;
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;
        var ageMinutes = (DateTime.UtcNow - issuedAt).TotalMinutes;

        if (ageMinutes > MaxTokenAgeMinutes)
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "step_up_required",
                message = $"กรุณายืนยันตัวตนอีกครั้ง (token อายุ {(int)ageMinutes} นาที — เกิน {MaxTokenAgeMinutes} นาที)",
                tokenAgeMinutes = (int)ageMinutes,
                maxAgeMinutes = MaxTokenAgeMinutes,
            });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
