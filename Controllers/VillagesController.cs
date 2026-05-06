using Gateway.Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #26 Phase 3 — Villages master CRUD + lookup. DOPA 8-digit codes
/// (PPDDSSMM) for cross-reference with กรมการปกครอง household data (ทร.14).
/// </summary>
[ApiController]
[Route("api/v1/villages")]
[Authorize]
public class VillagesController : ControllerBase
{
    private readonly GatewayDbContext _db;
    public VillagesController(SbdDbContext db) { _db = (GatewayDbContext)db; }

    /// <summary>
    /// List/search villages. Filter by subdistrictId (most common — picker for school
    /// service area), or by free-text search (name or code).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<VillageDto>>> List(
        [FromQuery] int? subdistrictId,
        [FromQuery] string? search,
        [FromQuery] int limit = 100)
    {
        var query = _db.Villages.AsNoTracking().Where(v => v.IsActive);
        if (subdistrictId.HasValue) query = query.Where(v => v.SubDistrictId == subdistrictId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(v => v.NameTh.Contains(s) || (v.Code != null && v.Code.Contains(s)));
        }
        var rows = await query.OrderBy(v => v.SubDistrictId).ThenBy(v => v.MooNo)
            .Take(Math.Min(limit, 500))
            .Select(v => new VillageDto
            {
                Id = v.Id,
                SubDistrictId = v.SubDistrictId,
                MooNo = v.MooNo,
                NameTh = v.NameTh,
                Code = v.Code,
            })
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>Create a new village (school admin can register a new หมู่ที่ for picker).</summary>
    [HttpPost]
    public async Task<ActionResult<VillageDto>> Create([FromBody] VillageCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NameTh))
            return BadRequest(new { message = "NameTh required" });

        // Check unique constraint (subdistrict, moo_no)
        var dup = await _db.Villages.AnyAsync(v =>
            v.SubDistrictId == req.SubDistrictId && v.MooNo == req.MooNo);
        if (dup)
            return Conflict(new { message = $"หมู่ {req.MooNo} ในตำบลนี้มีอยู่แล้ว" });

        var v = new Village
        {
            SubDistrictId = req.SubDistrictId,
            MooNo = req.MooNo,
            NameTh = req.NameTh.Trim(),
            Code = string.IsNullOrWhiteSpace(req.Code) ? null : req.Code.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Villages.Add(v);
        await _db.SaveChangesAsync();
        return Ok(new VillageDto
        {
            Id = v.Id, SubDistrictId = v.SubDistrictId,
            MooNo = v.MooNo, NameTh = v.NameTh, Code = v.Code,
        });
    }
}

public class VillageDto
{
    public int Id { get; set; }
    public int SubDistrictId { get; set; }
    public int MooNo { get; set; }
    public string NameTh { get; set; } = string.Empty;
    public string? Code { get; set; }
}

public class VillageCreateRequest
{
    public int SubDistrictId { get; set; }
    public int MooNo { get; set; }
    public string NameTh { get; set; } = string.Empty;
    public string? Code { get; set; }
}
