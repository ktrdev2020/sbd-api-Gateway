using System.Text.Json;
using System.Text.Json.Serialization;
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

/// <summary>
/// Plan #26 Phase 3 — DOPA village seeder. Fetches official open data from
/// catalog.dopa.go.th (gis-01 dataset) and imports into Villages master.
/// Idempotent: skips existing village codes; matches subdistrict by (district.Code + subdistrict.NameTh)
/// since SBD's SubDistricts.Code uses internal alphabetical numbering, not DOPA tcode.
/// </summary>
[ApiController]
[Route("api/v1/villages/seed-from-dopa")]
[Authorize]  // TODO: restrict to SuperAdmin once HCD wired
public class VillagesSeederController : ControllerBase
{
    private readonly GatewayDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<VillagesSeederController> _logger;

    public VillagesSeederController(SbdDbContext db, IHttpClientFactory httpFactory, ILogger<VillagesSeederController> logger)
    {
        _db = (GatewayDbContext)db;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(20_000_000)]   // 20 MB — DOPA province JSON is ~6 MB
    public async Task<ActionResult<DopaSeedResult>> SeedFromDopa(
        [FromQuery] string? url,
        [FromQuery] string? districtFilter,  // optional comma-separated district codes (e.g. "3305,3322,3326")
        [FromBody] List<DopaVillageRow>? body = null)
    {
        List<DopaVillageRow>? rows = null;

        // Either fetch from URL OR use POSTed JSON body
        if (body != null && body.Count > 0)
        {
            rows = body;
            _logger.LogInformation("DOPA seeder: using POSTed body with {Count} rows", body.Count);
        }
        else if (!string.IsNullOrWhiteSpace(url))
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(2);
            string json;
            try { json = await http.GetStringAsync(url); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DOPA seeder: fetch failed for url={Url}", url);
                return BadRequest(new { message = $"Fetch failed: {ex.Message}" });
            }
            try
            {
                rows = JsonSerializer.Deserialize<List<DopaVillageRow>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DOPA seeder: parse failed");
                return BadRequest(new { message = $"Parse failed: {ex.Message}" });
            }
        }
        else
        {
            return BadRequest(new { message = "Either ?url= query OR JSON body required" });
        }
        if (rows == null) return BadRequest(new { message = "Empty/null JSON" });

        var allowedDistricts = string.IsNullOrWhiteSpace(districtFilter)
            ? null : districtFilter.Split(',').Select(s => s.Trim()).ToHashSet();

        // Build subdistrict lookup: key = "{districtCode}|{subdistrictNameTh}"
        var subdistMap = await _db.SubDistricts.AsNoTracking()
            .Include(sd => sd.District)
            .Select(sd => new { sd.Id, sd.NameTh, DistrictCode = sd.District.Code })
            .ToListAsync();
        var subdistByKey = subdistMap.ToDictionary(
            x => $"{x.DistrictCode}|{x.NameTh}",
            x => x.Id);

        // Existing villages keyed by (SubDistrictId, MooNo) for upsert
        var existingByKey = (await _db.Villages
            .Where(v => true)
            .ToListAsync())
            .ToDictionary(v => $"{v.SubDistrictId}|{v.MooNo}", v => v);

        int inserted = 0, updated = 0, skippedNoSubdist = 0, skippedFiltered = 0, skippedBadCode = 0;
        var unmappedSubdistricts = new HashSet<string>();
        var addedKeysThisBatch = new HashSet<string>();

        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.mcode) || string.IsNullOrWhiteSpace(r.mname)) continue;
            if (allowedDistricts != null && !allowedDistricts.Contains(r.acode ?? "")) { skippedFiltered++; continue; }

            // Parse moo from mcode last 2 digits
            if (r.mcode.Length < 8 || !int.TryParse(r.mcode[^2..], out var moo) || moo < 1) { skippedBadCode++; continue; }

            var subKey = $"{r.acode}|{r.tname}";
            if (!subdistByKey.TryGetValue(subKey, out var sdId))
            {
                skippedNoSubdist++;
                unmappedSubdistricts.Add(subKey);
                continue;
            }

            var rowKey = $"{sdId}|{moo}";
            if (addedKeysThisBatch.Contains(rowKey)) continue;  // dup within batch
            addedKeysThisBatch.Add(rowKey);

            var nameTh = r.mname.StartsWith("บ้าน") ? r.mname : "บ้าน" + r.mname;

            if (existingByKey.TryGetValue(rowKey, out var existing))
            {
                // Upsert: refresh Code + NameTh if different
                if (existing.Code != r.mcode || existing.NameTh != nameTh)
                {
                    existing.Code = r.mcode;
                    existing.NameTh = nameTh;
                    updated++;
                }
            }
            else
            {
                _db.Villages.Add(new Village
                {
                    SubDistrictId = sdId,
                    MooNo = moo,
                    NameTh = nameTh,
                    Code = r.mcode,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                inserted++;
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DOPA seeder: SaveChanges failed");
            return StatusCode(500, new { message = $"DB save failed: {ex.Message}", inner = ex.InnerException?.Message });
        }

        return Ok(new DopaSeedResult
        {
            TotalRows = rows.Count,
            Inserted = inserted,
            Updated = updated,
            SkippedNoSubdistMatch = skippedNoSubdist,
            SkippedFiltered = skippedFiltered,
            SkippedBadCode = skippedBadCode,
            UnmappedSubdistrictKeys = unmappedSubdistricts.Take(20).ToList(),
        });
    }

    public class DopaVillageRow
    {
        [JsonPropertyName("pcode")] public string? pcode { get; set; }
        [JsonPropertyName("pname")] public string? pname { get; set; }
        [JsonPropertyName("acode")] public string? acode { get; set; }
        [JsonPropertyName("aname")] public string? aname { get; set; }
        [JsonPropertyName("tcode")] public string? tcode { get; set; }
        [JsonPropertyName("tname")] public string? tname { get; set; }
        [JsonPropertyName("mcode")] public string? mcode { get; set; }
        [JsonPropertyName("mname")] public string? mname { get; set; }
    }
}

public class DopaSeedResult
{
    public int TotalRows { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int SkippedNoSubdistMatch { get; set; }
    public int SkippedFiltered { get; set; }
    public int SkippedBadCode { get; set; }
    public List<string> UnmappedSubdistrictKeys { get; set; } = new();
}
