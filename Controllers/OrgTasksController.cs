using Gateway.Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #46 — งานในฝ่ายงานของโรงเรียน (line-items shown under each of the 4
/// school divisions). CRUD scoped per WorkGroup. School-scope authz is enforced
/// at composite GET; this controller validates the parent WorkGroup is a School
/// scope and trusts the upstream auth context.
/// </summary>
[ApiController]
[Route("api/v1/workgroup/{wgId:int}/tasks")]
[Authorize]
public class OrgTasksController : ControllerBase
{
    private readonly GatewayDbContext _db;
    public OrgTasksController(SbdDbContext db) { _db = (GatewayDbContext)db; }

    [HttpGet]
    public async Task<ActionResult<List<OrgTaskDto>>> List(int wgId)
    {
        var wg = await _db.WorkGroups.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == wgId);
        if (wg == null) return NotFound(new { message = "WorkGroup not found" });

        var rows = await _db.SchoolOrgTasks.AsNoTracking()
            .Where(t => t.WorkGroupId == wgId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
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
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<OrgTaskDto>> Create(int wgId, [FromBody] OrgTaskUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NameTh))
            return BadRequest(new { message = "NameTh is required" });

        var wg = await _db.WorkGroups.FirstOrDefaultAsync(w => w.Id == wgId);
        if (wg == null) return NotFound(new { message = "WorkGroup not found" });
        if (wg.ScopeType != "School")
            return BadRequest(new { message = "OrgTask is only supported for ScopeType=School" });

        var nextSort = request.SortOrder ?? ((await _db.SchoolOrgTasks
            .Where(t => t.WorkGroupId == wgId)
            .MaxAsync(t => (int?)t.SortOrder) ?? 0) + 1);

        var task = new SchoolOrgTask
        {
            WorkGroupId = wgId,
            NameTh = request.NameTh.Trim(),
            Description = request.Description,
            SortOrder = nextSort,
            IsActive = request.IsActive ?? true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.SchoolOrgTasks.Add(task);
        await _db.SaveChangesAsync();

        return Ok(new OrgTaskDto
        {
            Id = task.Id,
            WorkGroupId = task.WorkGroupId,
            NameTh = task.NameTh,
            Description = task.Description,
            SortOrder = task.SortOrder,
            IsActive = task.IsActive,
        });
    }

    [HttpPut("{taskId:long}")]
    public async Task<ActionResult<OrgTaskDto>> Update(int wgId, long taskId, [FromBody] OrgTaskUpsertRequest request)
    {
        var task = await _db.SchoolOrgTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.WorkGroupId == wgId);
        if (task == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.NameTh)) task.NameTh = request.NameTh.Trim();
        if (request.Description != null) task.Description = request.Description;
        if (request.SortOrder.HasValue) task.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue) task.IsActive = request.IsActive.Value;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new OrgTaskDto
        {
            Id = task.Id,
            WorkGroupId = task.WorkGroupId,
            NameTh = task.NameTh,
            Description = task.Description,
            SortOrder = task.SortOrder,
            IsActive = task.IsActive,
        });
    }

    [HttpDelete("{taskId:long}")]
    public async Task<ActionResult> Delete(int wgId, long taskId)
    {
        var task = await _db.SchoolOrgTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.WorkGroupId == wgId);
        if (task == null) return NotFound();

        _db.SchoolOrgTasks.Remove(task);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("reorder")]
    public async Task<ActionResult> Reorder(int wgId, [FromBody] List<OrgTaskReorderRow> rows)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var tasks = await _db.SchoolOrgTasks
            .Where(t => t.WorkGroupId == wgId && ids.Contains(t.Id))
            .ToListAsync();

        var lookup = rows.ToDictionary(r => r.Id, r => r.SortOrder);
        foreach (var t in tasks)
        {
            if (lookup.TryGetValue(t.Id, out var newSort))
            {
                t.SortOrder = newSort;
                t.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class OrgTaskDto
{
    public long Id { get; set; }
    public int WorkGroupId { get; set; }
    public string NameTh { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public record OrgTaskUpsertRequest(string? NameTh, string? Description, int? SortOrder, bool? IsActive);
public record OrgTaskReorderRow(long Id, int SortOrder);
