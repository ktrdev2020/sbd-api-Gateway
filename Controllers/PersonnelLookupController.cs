using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #11 X3 — batch personnel hydration endpoint.
///
/// Frontend stores PersonnelId-only references in BudgetApi DB and hydrates
/// display names on demand via this batch endpoint. Up to 200 ids per call.
///
/// Used by aplan projects-list, project-form, activity-tree (replacing the
/// dropped PersonnelName cache columns in budget tables).
///
/// Cache: 5-min frontend cache (Map&lt;id, name&gt;) collapses repeat requests.
/// Server-side caching is intentionally skipped — POST is non-cacheable per
/// HTTP spec, and per-user authorization scope makes a shared cache risky.
/// </summary>
[ApiController]
[Route("api/v1/personnel/by-ids")]
[Authorize]
public class PersonnelLookupController : ControllerBase
{
    private readonly SbdDbContext _context;
    private const int MaxIds = 200;

    public PersonnelLookupController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Resolve a batch of PersonnelIds → display info (id, fullName, position, schoolCode/Name).
    /// Returns rows in same order as input ids; missing ids are silently omitted.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<List<PersonnelLookupDto>>> ResolveBatch(
        [FromBody] PersonnelLookupRequest request,
        CancellationToken ct)
    {
        if (request?.Ids == null || request.Ids.Count == 0)
        {
            return Ok(new List<PersonnelLookupDto>());
        }

        if (request.Ids.Count > MaxIds)
        {
            return BadRequest(new { error = $"Too many ids — max {MaxIds} per call, got {request.Ids.Count}" });
        }

        var distinctIds = request.Ids.Distinct().ToList();

        // Single round-trip: load Personnel + nav, project to DTO server-side.
        var rows = await _context.Personnel
            .AsNoTracking()
            .Where(p => distinctIds.Contains(p.Id))
            .Select(p => new PersonnelLookupDto
            {
                Id = p.Id,
                FullName = (p.TitlePrefix != null ? p.TitlePrefix.NameTh : "")
                           + p.FirstName + " " + p.LastName,
                Position = p.PositionType != null ? p.PositionType.NameTh : null,
                PersonnelType = p.PersonnelTypeNav != null ? p.PersonnelTypeNav.NameTh : null,
                SchoolCode = p.SchoolAssignments
                    .Where(a => a.IsPrimary)
                    .Select(a => a.SchoolCode)
                    .FirstOrDefault(),
                SchoolName = p.SchoolAssignments
                    .Where(a => a.IsPrimary && a.School != null)
                    .Select(a => a.School!.NameTh)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Ok(rows);
    }
}

public class PersonnelLookupRequest
{
    public List<int> Ids { get; set; } = new();
}

public class PersonnelLookupDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public string? PersonnelType { get; set; }
    public string? SchoolCode { get; set; }
    public string? SchoolName { get; set; }
}
