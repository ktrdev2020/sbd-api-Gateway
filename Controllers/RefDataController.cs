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
}
