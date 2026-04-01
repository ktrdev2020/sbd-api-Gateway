using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/position-type")]
[Authorize]
public class PositionTypeController : ControllerBase
{
    private readonly SbdDbContext _context;

    public PositionTypeController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>Get all position types (centrally managed reference data).</summary>
    [HttpGet]
    public async Task<ActionResult> GetAll([FromQuery] string? category)
    {
        var query = _context.PositionTypes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(category))
            query = query.Where(p => p.Category == category);

        var positions = await query
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.NameTh)
            .Select(p => new
            {
                p.Id, p.Code, p.NameTh, p.NameEn, p.Category,
                p.IsSchoolDirector, p.SortOrder, p.IsActive
            })
            .ToListAsync();

        return Ok(positions);
    }

    /// <summary>Create a position type (SuperAdmin only).</summary>
    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreatePositionTypeRequest request)
    {
        var exists = await _context.PositionTypes.AnyAsync(p => p.Code == request.Code);
        if (exists) return Conflict(new { message = $"Position code '{request.Code}' already exists" });

        var maxSort = await _context.PositionTypes.MaxAsync(p => (int?)p.SortOrder) ?? 0;

        var position = new PositionType
        {
            Code = request.Code,
            NameTh = request.NameTh,
            NameEn = request.NameEn,
            Category = request.Category,
            IsSchoolDirector = request.IsSchoolDirector,
            SortOrder = maxSort + 1
        };
        _context.PositionTypes.Add(position);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), null, new { position.Id, position.Code, position.NameTh });
    }

    /// <summary>Update a position type.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, [FromBody] UpdatePositionTypeRequest request)
    {
        var position = await _context.PositionTypes.FindAsync(id);
        if (position == null) return NotFound();

        if (request.NameTh != null) position.NameTh = request.NameTh;
        if (request.NameEn != null) position.NameEn = request.NameEn;
        if (request.Category != null) position.Category = request.Category;
        if (request.IsSchoolDirector.HasValue) position.IsSchoolDirector = request.IsSchoolDirector.Value;
        if (request.IsActive.HasValue) position.IsActive = request.IsActive.Value;

        await _context.SaveChangesAsync();
        return Ok(new { position.Id, position.NameTh });
    }

    /// <summary>Delete a position type.</summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var position = await _context.PositionTypes.FindAsync(id);
        if (position == null) return NotFound();

        _context.PositionTypes.Remove(position);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

public record CreatePositionTypeRequest(string Code, string NameTh, string? NameEn, string Category, bool IsSchoolDirector = false);
public record UpdatePositionTypeRequest(string? NameTh, string? NameEn, string? Category, bool? IsSchoolDirector, bool? IsActive);
