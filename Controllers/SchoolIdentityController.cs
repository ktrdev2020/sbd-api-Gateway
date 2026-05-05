using Gateway.Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #26 — School profile expansion (วิสัยทัศน์ / พันธกิจ / เป้าประสงค์ / ยุทธศาสตร์ /
/// คำขวัญ / ปรัชญา / อักษรย่อ / สี / ต้นไม้ / ดอกไม้ / ชุดนักเรียน) per fiscal year.
/// Source: aplan PDF บทที่ 3.
/// </summary>
[ApiController]
[Route("api/v1/school/{schoolCode}/identity")]
[Authorize]
public class SchoolIdentityController : ControllerBase
{
    private readonly GatewayDbContext _db;

    // GatewayDbContext is registered as SbdDbContext in DI — cast on constructor.
    public SchoolIdentityController(SbdDbContext db) { _db = (GatewayDbContext)db; }

    [HttpGet]
    public async Task<ActionResult<SchoolIdentityDto>> Get(string schoolCode, [FromQuery] int? year)
    {
        var fiscalYear = year ?? CurrentFiscalYear();
        var identity = await _db.SchoolIdentities
            .Include(i => i.Missions.OrderBy(m => m.SortOrder))
            .Include(i => i.Goals.OrderBy(g => g.SortOrder))
            .Include(i => i.Strategies.OrderBy(s => s.SortOrder))
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.SchoolCode == schoolCode && i.FiscalYear == fiscalYear);

        if (identity == null)
        {
            // Return empty shell — frontend can save to create.
            return Ok(new SchoolIdentityDto
            {
                SchoolCode = schoolCode,
                FiscalYear = fiscalYear,
                Missions = new(),
                Goals = new(),
                Strategies = new()
            });
        }

        return Ok(MapToDto(identity));
    }

    [HttpPut]
    public async Task<ActionResult<SchoolIdentityDto>> Upsert(
        string schoolCode, [FromQuery] int? year, [FromBody] SchoolIdentityUpsertRequest req)
    {
        var fiscalYear = year ?? CurrentFiscalYear();

        var identity = await _db.SchoolIdentities
            .Include(i => i.Missions)
            .Include(i => i.Goals)
            .Include(i => i.Strategies)
            .FirstOrDefaultAsync(i => i.SchoolCode == schoolCode && i.FiscalYear == fiscalYear);

        if (identity == null)
        {
            identity = new SchoolIdentity
            {
                SchoolCode = schoolCode,
                FiscalYear = fiscalYear,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _db.SchoolIdentities.Add(identity);
        }

        identity.Vision = req.Vision;
        identity.Philosophy = req.Philosophy;
        identity.Slogan = req.Slogan;
        identity.Abbreviation = req.Abbreviation;
        identity.SchoolColors = req.SchoolColors;
        identity.SchoolTree = req.SchoolTree;
        identity.SchoolFlower = req.SchoolFlower;
        identity.UniformDescription = req.UniformDescription;
        identity.UpdatedAt = DateTimeOffset.UtcNow;

        // Replace lists wholesale (atomic per resource — D6)
        identity.Missions.Clear();
        identity.Goals.Clear();
        identity.Strategies.Clear();
        await _db.SaveChangesAsync();   // Persist clears so child rows are deleted before re-add

        if (req.Missions != null)
            for (int i = 0; i < req.Missions.Count; i++)
                identity.Missions.Add(new SchoolMission { IdentityId = identity.Id, SortOrder = i, Description = req.Missions[i] });
        if (req.Goals != null)
            for (int i = 0; i < req.Goals.Count; i++)
                identity.Goals.Add(new SchoolGoal { IdentityId = identity.Id, SortOrder = i, Description = req.Goals[i] });
        if (req.Strategies != null)
            for (int i = 0; i < req.Strategies.Count; i++)
                identity.Strategies.Add(new SchoolStrategy { IdentityId = identity.Id, SortOrder = i, Description = req.Strategies[i] });

        await _db.SaveChangesAsync();

        return Ok(MapToDto(identity));
    }

    private static int CurrentFiscalYear()
    {
        // ปีการศึกษาไทย = ปี พ.ศ. ที่เริ่มภาคเรียน 1 (พฤษภาคม)
        var now = DateTime.Now;
        var year = now.Year + 543;
        if (now.Month < 5) year -= 1;  // ก่อน พ.ค. ยังถือเป็นปีการศึกษาก่อน
        return year;
    }

    private static SchoolIdentityDto MapToDto(SchoolIdentity i) => new()
    {
        Id = i.Id,
        SchoolCode = i.SchoolCode,
        FiscalYear = i.FiscalYear,
        Vision = i.Vision,
        Philosophy = i.Philosophy,
        Slogan = i.Slogan,
        Abbreviation = i.Abbreviation,
        SchoolColors = i.SchoolColors,
        SchoolTree = i.SchoolTree,
        SchoolFlower = i.SchoolFlower,
        UniformDescription = i.UniformDescription,
        Missions = i.Missions.OrderBy(m => m.SortOrder).Select(m => m.Description).ToList(),
        Goals = i.Goals.OrderBy(g => g.SortOrder).Select(g => g.Description).ToList(),
        Strategies = i.Strategies.OrderBy(s => s.SortOrder).Select(s => s.Description).ToList(),
    };
}

public class SchoolIdentityDto
{
    public long Id { get; set; }
    public string SchoolCode { get; set; } = string.Empty;
    public int FiscalYear { get; set; }
    public string? Vision { get; set; }
    public string? Philosophy { get; set; }
    public string? Slogan { get; set; }
    public string? Abbreviation { get; set; }
    public string? SchoolColors { get; set; }
    public string? SchoolTree { get; set; }
    public string? SchoolFlower { get; set; }
    public string? UniformDescription { get; set; }
    public List<string> Missions { get; set; } = new();
    public List<string> Goals { get; set; } = new();
    public List<string> Strategies { get; set; } = new();
}

public class SchoolIdentityUpsertRequest
{
    public string? Vision { get; set; }
    public string? Philosophy { get; set; }
    public string? Slogan { get; set; }
    public string? Abbreviation { get; set; }
    public string? SchoolColors { get; set; }
    public string? SchoolTree { get; set; }
    public string? SchoolFlower { get; set; }
    public string? UniformDescription { get; set; }
    public List<string>? Missions { get; set; }
    public List<string>? Goals { get; set; }
    public List<string>? Strategies { get; set; }
}
