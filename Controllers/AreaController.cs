using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/area")]
[Authorize]
public class AreaController : ControllerBase
{
    private readonly SbdDbContext _context;

    public AreaController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get all education areas (เขตพื้นที่การศึกษา).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AreaListDto>>> GetAreas()
    {
        var areas = await _context.Areas
            .AsNoTracking()
            .OrderBy(a => a.Code)
            .Select(a => new AreaListDto
            {
                Id = a.Id,
                Code = a.Code,
                NameTh = a.NameTh,
                NameEn = null,
            })
            .ToListAsync();

        return Ok(areas);
    }

    /// <summary>
    /// Get a single area by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AreaListDto>> GetArea(int id)
    {
        var area = await _context.Areas
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AreaListDto
            {
                Id = a.Id,
                Code = a.Code,
                NameTh = a.NameTh,
                NameEn = null,
            })
            .FirstOrDefaultAsync();

        if (area is null)
            return NotFound(new { message = "Area not found" });

        return Ok(area);
    }
}

public class AreaListDto
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string NameTh { get; set; }
    public string? NameEn { get; set; }
}
