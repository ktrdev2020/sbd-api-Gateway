using Gateway.Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #26 Phase 3 — School community context + service villages + occupations + board members.
/// Source: aplan PDF บทที่ 2.2-2.7.
/// </summary>
[ApiController]
[Route("api/v1/school/{schoolCode}/community")]
[Authorize]
public class SchoolCommunityController : ControllerBase
{
    private readonly GatewayDbContext _db;
    public SchoolCommunityController(SbdDbContext db) { _db = (GatewayDbContext)db; }

    private static int CurrentFiscalYear()
    {
        var now = DateTime.Now;
        var y = now.Year + 543;
        if (now.Month < 5) y -= 1;
        return y;
    }

    /// <summary>
    /// Returns the school's address foreign keys so the village picker can filter by subdistrict.
    /// SchoolDto's nested AddressDto exposes only names, not IDs — this exposes the IDs.
    /// </summary>
    [HttpGet("address-context")]
    public async Task<ActionResult<SchoolAddressContextDto>> GetAddressContext(string schoolCode)
    {
        var row = await _db.Schools.AsNoTracking()
            .Where(s => s.SchoolCode == schoolCode)
            .Include(s => s.Address)
                .ThenInclude(a => a!.SubDistrict)
                    .ThenInclude(sd => sd.District)
                        .ThenInclude(d => d.Province)
            .Select(s => new SchoolAddressContextDto
            {
                SchoolCode = s.SchoolCode,
                SubDistrictId = s.Address != null ? s.Address.SubDistrictId : (int?)null,
                SubDistrictName = s.Address != null ? s.Address.SubDistrict.NameTh : null,
                DistrictId = s.Address != null && s.Address.SubDistrict != null
                    ? s.Address.SubDistrict.DistrictId : (int?)null,
                DistrictName = s.Address != null && s.Address.SubDistrict != null
                    ? s.Address.SubDistrict.District.NameTh : null,
                ProvinceId = s.Address != null && s.Address.SubDistrict != null
                                && s.Address.SubDistrict.District != null
                    ? s.Address.SubDistrict.District.ProvinceId : (int?)null,
                ProvinceName = s.Address != null && s.Address.SubDistrict != null
                                && s.Address.SubDistrict.District != null
                    ? s.Address.SubDistrict.District.Province.NameTh : null,
            })
            .FirstOrDefaultAsync();

        if (row == null) return NotFound(new { message = "School not found" });
        return Ok(row);
    }

    // ─── Community context (1:1) — includes service villages + occupations ──────

    [HttpGet]
    public async Task<ActionResult<SchoolCommunityDto>> Get(string schoolCode, [FromQuery] int? year)
    {
        var fiscalYear = year ?? CurrentFiscalYear();
        var ctx = await _db.SchoolCommunityContexts
            .Include(c => c.ServiceVillages.OrderBy(v => v.SortOrder))
            .Include(c => c.Occupations.OrderBy(o => o.SortOrder))
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SchoolCode == schoolCode && c.FiscalYear == fiscalYear);

        if (ctx == null)
        {
            return Ok(new SchoolCommunityDto
            {
                SchoolCode = schoolCode, FiscalYear = fiscalYear,
                ServiceVillages = new(), Occupations = new(),
            });
        }

        // Resolve village info for each service village
        var villageIds = ctx.ServiceVillages.Select(v => v.VillageId).Distinct().ToList();
        var villages = await _db.Villages.AsNoTracking()
            .Where(v => villageIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);

        return Ok(new SchoolCommunityDto
        {
            Id = ctx.Id,
            SchoolCode = ctx.SchoolCode,
            FiscalYear = ctx.FiscalYear,
            SubdistrictMalePopulation = ctx.SubdistrictMalePopulation,
            SubdistrictFemalePopulation = ctx.SubdistrictFemalePopulation,
            SubdistrictHouseholdCount = ctx.SubdistrictHouseholdCount,
            GeographyDescription = ctx.GeographyDescription,
            ClimateDescription = ctx.ClimateDescription,
            EconomyDescription = ctx.EconomyDescription,
            ReligionCultureDescription = ctx.ReligionCultureDescription,
            AverageIncomePerHousehold = ctx.AverageIncomePerHousehold,
            Notes = ctx.Notes,
            ServiceVillages = ctx.ServiceVillages.Select(sv =>
            {
                villages.TryGetValue(sv.VillageId, out var v);
                return new ServiceVillageDto
                {
                    Id = sv.Id,
                    VillageId = sv.VillageId,
                    MooNo = v?.MooNo ?? 0,
                    VillageName = v?.NameTh ?? "(หมู่บ้านถูกลบ)",
                    VillageCode = v?.Code,
                    HeadmanName = sv.HeadmanName,
                    HeadmanPhone = sv.HeadmanPhone,
                    MaleCount = sv.MaleCount,
                    FemaleCount = sv.FemaleCount,
                    HouseholdCount = sv.HouseholdCount,
                    SortOrder = sv.SortOrder,
                    Notes = sv.Notes,
                };
            }).ToList(),
            Occupations = ctx.Occupations.Select(o => new OccupationDto
            {
                Id = o.Id,
                OccupationName = o.OccupationName,
                HouseholdCount = o.HouseholdCount,
                Percentage = o.Percentage,
                SortOrder = o.SortOrder,
            }).ToList(),
        });
    }

    [HttpPut]
    public async Task<ActionResult<SchoolCommunityDto>> Upsert(
        string schoolCode, [FromQuery] int? year, [FromBody] SchoolCommunityUpsertRequest req)
    {
        var fiscalYear = year ?? CurrentFiscalYear();
        var ctx = await _db.SchoolCommunityContexts
            .FirstOrDefaultAsync(c => c.SchoolCode == schoolCode && c.FiscalYear == fiscalYear);

        if (ctx == null)
        {
            ctx = new SchoolCommunityContext
            {
                SchoolCode = schoolCode, FiscalYear = fiscalYear,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            _db.SchoolCommunityContexts.Add(ctx);
        }

        ctx.SubdistrictMalePopulation = req.SubdistrictMalePopulation;
        ctx.SubdistrictFemalePopulation = req.SubdistrictFemalePopulation;
        ctx.SubdistrictHouseholdCount = req.SubdistrictHouseholdCount;
        ctx.GeographyDescription = req.GeographyDescription;
        ctx.ClimateDescription = req.ClimateDescription;
        ctx.EconomyDescription = req.EconomyDescription;
        ctx.ReligionCultureDescription = req.ReligionCultureDescription;
        ctx.AverageIncomePerHousehold = req.AverageIncomePerHousehold;
        ctx.Notes = req.Notes;
        ctx.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();   // ensure ctx.Id is generated for first save

        // Bulk replace child lists via direct DbSet.RemoveRange + Add
        // (Pattern matches SaveBoard — avoids EF Core navigation quirks where
        // ctx.Occupations.Add() after Clear() sometimes doesn't register the INSERT
        // when the parent was just-loaded with .Include.)
        var existingVillages = _db.SchoolServiceVillages.Where(v => v.ContextId == ctx.Id);
        _db.SchoolServiceVillages.RemoveRange(existingVillages);
        var existingOcc = _db.SchoolMajorOccupations.Where(o => o.ContextId == ctx.Id);
        _db.SchoolMajorOccupations.RemoveRange(existingOcc);
        await _db.SaveChangesAsync();

        if (req.ServiceVillages != null)
        {
            for (int i = 0; i < req.ServiceVillages.Count; i++)
            {
                var sv = req.ServiceVillages[i];
                _db.SchoolServiceVillages.Add(new SchoolServiceVillage
                {
                    ContextId = ctx.Id,
                    VillageId = sv.VillageId,
                    HeadmanName = sv.HeadmanName,
                    HeadmanPhone = sv.HeadmanPhone,
                    MaleCount = sv.MaleCount,
                    FemaleCount = sv.FemaleCount,
                    HouseholdCount = sv.HouseholdCount,
                    SortOrder = i,
                    Notes = sv.Notes,
                });
            }
        }
        if (req.Occupations != null)
        {
            for (int i = 0; i < req.Occupations.Count; i++)
            {
                var o = req.Occupations[i];
                if (string.IsNullOrWhiteSpace(o.OccupationName)) continue;
                _db.SchoolMajorOccupations.Add(new SchoolMajorOccupation
                {
                    ContextId = ctx.Id,
                    OccupationName = o.OccupationName.Trim(),
                    HouseholdCount = o.HouseholdCount,
                    Percentage = o.Percentage,
                    SortOrder = i,
                });
            }
        }
        await _db.SaveChangesAsync();
        return await Get(schoolCode, fiscalYear);
    }

    // ─── Board members (1:N per fiscal year) ────────────────────────────────────

    [HttpGet("board")]
    public async Task<ActionResult<List<BoardMemberDto>>> ListBoard(
        string schoolCode, [FromQuery] int? year)
    {
        var fiscalYear = year ?? CurrentFiscalYear();
        var rows = await _db.SchoolBoardMembers.AsNoTracking()
            .Where(b => b.SchoolCode == schoolCode && b.FiscalYear == fiscalYear)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .Select(b => new BoardMemberDto
            {
                Id = b.Id,
                MemberName = b.MemberName,
                Role = b.Role,
                Representing = b.Representing,
                ContactPhone = b.ContactPhone,
                AppointedAt = b.AppointedAt,
                ExpiresAt = b.ExpiresAt,
                SortOrder = b.SortOrder,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpPut("board")]
    public async Task<ActionResult<List<BoardMemberDto>>> SaveBoard(
        string schoolCode, [FromQuery] int? year, [FromBody] List<BoardMemberUpsertRow> rows)
    {
        var fiscalYear = year ?? CurrentFiscalYear();
        var existing = _db.SchoolBoardMembers
            .Where(b => b.SchoolCode == schoolCode && b.FiscalYear == fiscalYear);
        _db.SchoolBoardMembers.RemoveRange(existing);
        await _db.SaveChangesAsync();

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (string.IsNullOrWhiteSpace(r.MemberName)) continue;
            _db.SchoolBoardMembers.Add(new SchoolBoardMember
            {
                SchoolCode = schoolCode,
                FiscalYear = fiscalYear,
                MemberName = r.MemberName.Trim(),
                Role = r.Role,
                Representing = r.Representing,
                ContactPhone = r.ContactPhone,
                AppointedAt = r.AppointedAt,
                ExpiresAt = r.ExpiresAt,
                SortOrder = i,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        await _db.SaveChangesAsync();
        return await ListBoard(schoolCode, fiscalYear);
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class SchoolCommunityDto
{
    public long Id { get; set; }
    public string SchoolCode { get; set; } = "";
    public int FiscalYear { get; set; }
    public int? SubdistrictMalePopulation { get; set; }
    public int? SubdistrictFemalePopulation { get; set; }
    public int? SubdistrictHouseholdCount { get; set; }
    public string? GeographyDescription { get; set; }
    public string? ClimateDescription { get; set; }
    public string? EconomyDescription { get; set; }
    public string? ReligionCultureDescription { get; set; }
    public decimal? AverageIncomePerHousehold { get; set; }
    public string? Notes { get; set; }
    public List<ServiceVillageDto> ServiceVillages { get; set; } = new();
    public List<OccupationDto> Occupations { get; set; } = new();
}

public class ServiceVillageDto
{
    public long Id { get; set; }
    public int VillageId { get; set; }
    public int MooNo { get; set; }
    public string VillageName { get; set; } = "";
    public string? VillageCode { get; set; }
    public string? HeadmanName { get; set; }
    public string? HeadmanPhone { get; set; }
    public int? MaleCount { get; set; }
    public int? FemaleCount { get; set; }
    public int? HouseholdCount { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
}

public class OccupationDto
{
    public long Id { get; set; }
    public string OccupationName { get; set; } = "";
    public int? HouseholdCount { get; set; }
    public decimal? Percentage { get; set; }
    public int SortOrder { get; set; }
}

public class SchoolCommunityUpsertRequest
{
    public int? SubdistrictMalePopulation { get; set; }
    public int? SubdistrictFemalePopulation { get; set; }
    public int? SubdistrictHouseholdCount { get; set; }
    public string? GeographyDescription { get; set; }
    public string? ClimateDescription { get; set; }
    public string? EconomyDescription { get; set; }
    public string? ReligionCultureDescription { get; set; }
    public decimal? AverageIncomePerHousehold { get; set; }
    public string? Notes { get; set; }
    public List<ServiceVillageUpsertRow>? ServiceVillages { get; set; }
    public List<OccupationUpsertRow>? Occupations { get; set; }
}

public class ServiceVillageUpsertRow
{
    public int VillageId { get; set; }
    public string? HeadmanName { get; set; }
    public string? HeadmanPhone { get; set; }
    public int? MaleCount { get; set; }
    public int? FemaleCount { get; set; }
    public int? HouseholdCount { get; set; }
    public string? Notes { get; set; }
}

public class OccupationUpsertRow
{
    public string OccupationName { get; set; } = "";
    public int? HouseholdCount { get; set; }
    public decimal? Percentage { get; set; }
}

public class BoardMemberDto
{
    public long Id { get; set; }
    public string MemberName { get; set; } = "";
    public string? Role { get; set; }
    public string? Representing { get; set; }
    public string? ContactPhone { get; set; }
    public DateOnly? AppointedAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public int SortOrder { get; set; }
}

public class BoardMemberUpsertRow
{
    public string MemberName { get; set; } = "";
    public string? Role { get; set; }
    public string? Representing { get; set; }
    public string? ContactPhone { get; set; }
    public DateOnly? AppointedAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
}

public class SchoolAddressContextDto
{
    public string SchoolCode { get; set; } = "";
    public int? SubDistrictId { get; set; }
    public string? SubDistrictName { get; set; }
    public int? DistrictId { get; set; }
    public string? DistrictName { get; set; }
    public int? ProvinceId { get; set; }
    public string? ProvinceName { get; set; }
}
