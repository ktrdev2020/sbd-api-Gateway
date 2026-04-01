using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/workgroup")]
[Authorize]
public class WorkGroupController : ControllerBase
{
    private readonly SbdDbContext _context;

    public WorkGroupController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>Get work groups for a scope (School or Area).</summary>
    [HttpGet]
    public async Task<ActionResult> GetWorkGroups([FromQuery] string scopeType, [FromQuery] int scopeId)
    {
        var groups = await _context.WorkGroups
            .Where(g => g.ScopeType == scopeType && g.ScopeId == scopeId)
            .Include(g => g.Members)
                .ThenInclude(m => m.Personnel)
            .OrderBy(g => g.SortOrder)
            .Select(g => new
            {
                g.Id, g.Name, g.Description, g.ScopeType, g.ScopeId, g.SortOrder, g.IsActive,
                MemberCount = g.Members.Count,
                Members = g.Members.Select(m => new
                {
                    m.Id, m.PersonnelId, m.Role, m.StartDate, m.EndDate,
                    PersonnelName = m.Personnel.FirstName + " " + m.Personnel.LastName,
                    m.Personnel.PersonnelType
                })
            })
            .ToListAsync();

        return Ok(groups);
    }

    /// <summary>Create a work group.</summary>
    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateWorkGroupRequest request)
    {
        var maxSort = await _context.WorkGroups
            .Where(g => g.ScopeType == request.ScopeType && g.ScopeId == request.ScopeId)
            .MaxAsync(g => (int?)g.SortOrder) ?? 0;

        var group = new WorkGroup
        {
            Name = request.Name,
            Description = request.Description,
            ScopeType = request.ScopeType,
            ScopeId = request.ScopeId,
            SortOrder = maxSort + 1
        };
        _context.WorkGroups.Add(group);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetWorkGroups), new { scopeType = group.ScopeType, scopeId = group.ScopeId },
            new { group.Id, group.Name, group.ScopeType, group.ScopeId });
    }

    /// <summary>Update a work group.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, [FromBody] UpdateWorkGroupRequest request)
    {
        var group = await _context.WorkGroups.FindAsync(id);
        if (group == null) return NotFound();

        if (request.Name != null) group.Name = request.Name;
        if (request.Description != null) group.Description = request.Description;
        if (request.SortOrder.HasValue) group.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue) group.IsActive = request.IsActive.Value;

        await _context.SaveChangesAsync();
        return Ok(new { group.Id, group.Name });
    }

    /// <summary>Delete a work group.</summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var group = await _context.WorkGroups.FindAsync(id);
        if (group == null) return NotFound();

        _context.WorkGroups.Remove(group);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Add a member to a work group.</summary>
    [HttpPost("{id:int}/members")]
    public async Task<ActionResult> AddMember(int id, [FromBody] AddWorkGroupMemberRequest request)
    {
        var group = await _context.WorkGroups.FindAsync(id);
        if (group == null) return NotFound(new { message = "Work group not found" });

        var exists = await _context.WorkGroupMembers
            .AnyAsync(m => m.WorkGroupId == id && m.PersonnelId == request.PersonnelId);
        if (exists) return Conflict(new { message = "Personnel already in this group" });

        var member = new WorkGroupMember
        {
            WorkGroupId = id,
            PersonnelId = request.PersonnelId,
            Role = request.Role,
            StartDate = request.StartDate ?? DateOnly.FromDateTime(DateTime.Today)
        };
        _context.WorkGroupMembers.Add(member);
        await _context.SaveChangesAsync();

        return Ok(new { member.Id, member.WorkGroupId, member.PersonnelId, member.Role });
    }

    /// <summary>Remove a member from a work group.</summary>
    [HttpDelete("{groupId:int}/members/{memberId:int}")]
    public async Task<ActionResult> RemoveMember(int groupId, int memberId)
    {
        var member = await _context.WorkGroupMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.WorkGroupId == groupId);
        if (member == null) return NotFound();

        _context.WorkGroupMembers.Remove(member);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateWorkGroupRequest(string Name, string? Description, string ScopeType, int ScopeId);
public record UpdateWorkGroupRequest(string? Name, string? Description, int? SortOrder, bool? IsActive);
public record AddWorkGroupMemberRequest(int PersonnelId, string? Role, DateOnly? StartDate);
