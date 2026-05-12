using Gateway.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #46 — Composite read of a school's administrative-structure chart.
/// Combines leadership (ผอ / รอง / รักษาการ), 4 division WorkGroups (with their
/// head + members + tasks), and the school board (current fiscal year).
/// </summary>
[ApiController]
[Route("api/v1/school/{schoolCode}/org-structure")]
[Authorize]
public class OrgStructureController : ControllerBase
{
    private readonly GatewayDbContext _db;
    public OrgStructureController(SbdDbContext db) { _db = (GatewayDbContext)db; }

    private static int CurrentFiscalYear()
    {
        var now = DateTime.Now;
        var y = now.Year + 543;
        if (now.Month < 5) y -= 1;
        return y;
    }

    [HttpGet]
    public async Task<ActionResult<OrgStructureDto>> Get(string schoolCode, [FromQuery] int? year)
    {
        if (!OrgScopeAuth.CanAccessSchool(User, schoolCode))
            return Forbid();

        var fiscalYear = year ?? CurrentFiscalYear();

        var school = await _db.Schools.AsNoTracking()
            .Where(s => s.SchoolCode == schoolCode)
            .Select(s => new { s.SchoolCode, s.NameTh })
            .FirstOrDefaultAsync();
        if (school == null) return NotFound(new { message = "School not found" });

        // WorkGroup.ScopeId is int — SchoolCode is a 10-digit numeric string (1.03B range, fits int32)
        if (!int.TryParse(school.SchoolCode, out var schoolScopeId))
            return BadRequest(new { message = $"SchoolCode {school.SchoolCode} is not numeric" });

        // ── Leadership (ผอ / รองผอ) — derived from Position text + SpecialRoleType ──
        var leadership = await _db.PersonnelSchoolAssignments.AsNoTracking()
            .Where(a => a.SchoolCode == schoolCode
                && (a.Position!.StartsWith("ผู้อำนวยการ")
                    || a.Position.StartsWith("รองผู้อำนวยการ")
                    || a.SpecialRoleType == "acting_director"
                    || a.SpecialRoleType == "deputy_director"
                    || a.SpecialRoleType == "acting_deputy"))
            .Select(a => new LeadershipDto
            {
                PersonnelId = a.PersonnelId,
                FullName = a.Personnel.FirstName + " " + a.Personnel.LastName,
                Position = a.Position,
                SpecialRoleType = a.SpecialRoleType,
                IsDirector = a.Position!.StartsWith("ผู้อำนวยการ") || a.SpecialRoleType == "acting_director",
                IsDeputy = a.Position.StartsWith("รองผู้อำนวยการ")
                    || a.SpecialRoleType == "deputy_director"
                    || a.SpecialRoleType == "acting_deputy",
            })
            .ToListAsync();

        // ── 4 Division WorkGroups + members + tasks ─────────────────────────
        var workGroups = await _db.WorkGroups.AsNoTracking()
            .Where(w => w.ScopeType == "School" && w.ScopeId == schoolScopeId)
            .OrderBy(w => w.SortOrder).ThenBy(w => w.Id)
            .Select(w => new OrgWorkGroupDto
            {
                Id = w.Id,
                Name = w.Name,
                Description = w.Description,
                SortOrder = w.SortOrder,
                IsActive = w.IsActive,
                Members = w.Members.Select(m => new OrgWorkGroupMemberDto
                {
                    Id = m.Id,
                    PersonnelId = m.PersonnelId,
                    FullName = m.Personnel.FirstName + " " + m.Personnel.LastName,
                    Role = m.Role,
                    StartDate = m.StartDate,
                    EndDate = m.EndDate,
                }).ToList(),
            })
            .ToListAsync();

        var wgIds = workGroups.Select(w => w.Id).ToList();
        var tasks = await _db.SchoolOrgTasks.AsNoTracking()
            .Where(t => wgIds.Contains(t.WorkGroupId))
            .OrderBy(t => t.WorkGroupId).ThenBy(t => t.SortOrder).ThenBy(t => t.Id)
            .Select(t => new OrgTaskDto
            {
                Id = t.Id,
                WorkGroupId = t.WorkGroupId,
                NameTh = t.NameTh,
                Description = t.Description,
                SortOrder = t.SortOrder,
                IsActive = t.IsActive,
            })
            .ToListAsync();
        var tasksByWg = tasks.GroupBy(t => t.WorkGroupId)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var wg in workGroups)
            wg.Tasks = tasksByWg.TryGetValue(wg.Id, out var lst) ? lst : new List<OrgTaskDto>();

        // ── Board members (current fiscal year) ────────────────────────────
        var board = await _db.SchoolBoardMembers.AsNoTracking()
            .Where(b => b.SchoolCode == schoolCode && b.FiscalYear == fiscalYear)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .Select(b => new OrgBoardMemberDto
            {
                Id = b.Id,
                MemberName = b.MemberName,
                Role = b.Role,
                Representing = b.Representing,
                SortOrder = b.SortOrder,
            })
            .ToListAsync();

        return Ok(new OrgStructureDto
        {
            SchoolCode = school.SchoolCode,
            SchoolName = school.NameTh,
            FiscalYear = fiscalYear,
            Leadership = leadership,
            WorkGroups = workGroups,
            Board = board,
        });
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class OrgStructureDto
{
    public string SchoolCode { get; set; } = "";
    public string SchoolName { get; set; } = "";
    public int FiscalYear { get; set; }
    public List<LeadershipDto> Leadership { get; set; } = new();
    public List<OrgWorkGroupDto> WorkGroups { get; set; } = new();
    public List<OrgBoardMemberDto> Board { get; set; } = new();
}

public class LeadershipDto
{
    public int PersonnelId { get; set; }
    public string FullName { get; set; } = "";
    public string? Position { get; set; }
    public string SpecialRoleType { get; set; } = "none";
    public bool IsDirector { get; set; }
    public bool IsDeputy { get; set; }
}

public class OrgWorkGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public List<OrgWorkGroupMemberDto> Members { get; set; } = new();
    public List<OrgTaskDto> Tasks { get; set; } = new();
}

public class OrgWorkGroupMemberDto
{
    public int Id { get; set; }
    public int PersonnelId { get; set; }
    public string FullName { get; set; } = "";
    public string? Role { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

public class OrgBoardMemberDto
{
    public long Id { get; set; }
    public string MemberName { get; set; } = "";
    public string? Role { get; set; }
    public string? Representing { get; set; }
    public int SortOrder { get; set; }
}
