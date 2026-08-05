namespace Gateway.Models;

/// <summary>
/// Plan #103 — User feedback captured from the floating FAB button on any page.
/// Context (url/module) is captured client-side; identity (user/role/scope) is
/// resolved server-side from JWT claims so reports can be grouped per page,
/// per module, and per role tier. Entries are triaged by SuperAdmin
/// (new → acknowledged → resolved/dismissed) and exportable as Markdown for
/// AI-agent analysis.
/// </summary>
public class FeedbackEntry
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "unknown";
    public string? SchoolCode { get; set; }
    public string? AreaId { get; set; }
    public required string Url { get; set; }
    public string? ModuleCode { get; set; }
    public string? PageTitle { get; set; }
    public required string Category { get; set; }   // bug | improvement | feature | data | other
    public required string Message { get; set; }
    public string? UserAgent { get; set; }
    public string? Viewport { get; set; }
    public string Status { get; set; } = "new";     // new | acknowledged | resolved | dismissed
    /// <summary>Internal triage note — never shown to the reporter.</summary>
    public string? AdminNote { get; set; }

    // Plan #111 A3 — the reply the reporter actually sees. Kept separate from
    // AdminNote so triage chatter can never leak to the user by accident.
    public string? AdminReply { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
    public int? RepliedByUserId { get; set; }
    /// <summary>Release the fix shipped in, e.g. "v1.0.0" — shown with the reply.</summary>
    public string? ResolvedVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
