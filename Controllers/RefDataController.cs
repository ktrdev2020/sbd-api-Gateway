using Gateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class RefDataController : ControllerBase
{
    private readonly SbdDbContext _context;
    private readonly ICacheService _cache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);

    public RefDataController(SbdDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    [HttpGet("title-prefixes")]
    public async Task<ActionResult<IEnumerable<TitlePrefix>>> GetTitlePrefixes()
    {
        const string cacheKey = "refdata:title-prefixes";
        var cached = await _cache.GetAsync<List<TitlePrefix>>(cacheKey);
        if (cached != null)
            return Ok(cached);

        var data = await _context.TitlePrefixes.AsNoTracking().ToListAsync();
        await _cache.SetAsync(cacheKey, data, _cacheExpiration);
        return Ok(data);
    }

    [HttpGet("provinces")]
    public async Task<ActionResult<IEnumerable<Province>>> GetProvinces()
    {
        const string cacheKey = "refdata:provinces";
        var cached = await _cache.GetAsync<List<Province>>(cacheKey);
        if (cached != null)
            return Ok(cached);

        var data = await _context.Provinces.AsNoTracking().ToListAsync();
        await _cache.SetAsync(cacheKey, data, _cacheExpiration);
        return Ok(data);
    }

    [HttpGet("districts")]
    public async Task<ActionResult<IEnumerable<District>>> GetDistricts()
    {
        const string cacheKey = "refdata:districts";
        var cached = await _cache.GetAsync<List<District>>(cacheKey);
        if (cached != null)
            return Ok(cached);

        var data = await _context.Districts.AsNoTracking().ToListAsync();
        await _cache.SetAsync(cacheKey, data, _cacheExpiration);
        return Ok(data);
    }

    [HttpGet("sub-districts")]
    public async Task<ActionResult<IEnumerable<SubDistrict>>> GetSubDistricts()
    {
        const string cacheKey = "refdata:sub-districts";
        var cached = await _cache.GetAsync<List<SubDistrict>>(cacheKey);
        if (cached != null)
            return Ok(cached);

        var data = await _context.SubDistricts.AsNoTracking().ToListAsync();
        await _cache.SetAsync(cacheKey, data, _cacheExpiration);
        return Ok(data);
    }

    [HttpGet("area-types")]
    public async Task<ActionResult<IEnumerable<AreaType>>> GetAreaTypes()
    {
        const string cacheKey = "refdata:area-types";
        var cached = await _cache.GetAsync<List<AreaType>>(cacheKey);
        if (cached != null)
            return Ok(cached);

        var data = await _context.AreaTypes.AsNoTracking().ToListAsync();
        await _cache.SetAsync(cacheKey, data, _cacheExpiration);
        return Ok(data);
    }

    [HttpGet("roles")]
    public async Task<ActionResult<IEnumerable<Role>>> GetRoles()
    {
        const string cacheKey = "refdata:roles";
        var cached = await _cache.GetAsync<List<Role>>(cacheKey);
        if (cached != null)
            return Ok(cached);

        var data = await _context.Roles.AsNoTracking().ToListAsync();
        await _cache.SetAsync(cacheKey, data, _cacheExpiration);
        return Ok(data);
    }

    [HttpGet("modules")]
    public async Task<ActionResult<IEnumerable<Module>>> GetModules()
    {
        const string cacheKey = "refdata:modules";
        var cached = await _cache.GetAsync<List<Module>>(cacheKey);
        if (cached != null)
            return Ok(cached);

        var data = await _context.Modules.AsNoTracking().ToListAsync();
        await _cache.SetAsync(cacheKey, data, _cacheExpiration);
        return Ok(data);
    }

    [HttpGet("academic-standings")]
    public async Task<ActionResult<IEnumerable<AcademicStandingType>>> GetAcademicStandings()
    {
        const string cacheKey = "refdata:academic-standings";
        var cached = await _cache.GetAsync<List<AcademicStandingType>>(cacheKey);
        if (cached != null)
            return Ok(cached);

        var data = await _context.AcademicStandingTypes.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Level)
            .ToListAsync();
        await _cache.SetAsync(cacheKey, data, _cacheExpiration);
        return Ok(data);
    }

    [HttpGet("position-types")]
    public async Task<ActionResult<IEnumerable<PositionType>>> GetPositionTypes([FromQuery] string? category)
    {
        const string cacheKey = "refdata:position-types";
        var cached = await _cache.GetAsync<List<PositionType>>(cacheKey);
        if (cached != null)
        {
            var filtered = string.IsNullOrEmpty(category)
                ? cached.Where(p => p.IsActive)
                : cached.Where(p => p.IsActive && p.Category == category);
            return Ok(filtered.OrderBy(p => p.SortOrder));
        }

        var data = await _context.PositionTypes.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
        await _cache.SetAsync(cacheKey, data, _cacheExpiration);

        var result = string.IsNullOrEmpty(category)
            ? data
            : data.Where(p => p.Category == category).ToList();
        return Ok(result);
    }
}
