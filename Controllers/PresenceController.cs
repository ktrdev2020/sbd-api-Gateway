using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using StackExchange.Redis;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/presence")]
[Authorize]
public class PresenceController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;
    private readonly SbdDbContext _db;

    public PresenceController(IConnectionMultiplexer redis, SbdDbContext db)
    {
        _redis = redis;
        _db = db;
    }

    /// <summary>Current snapshot: online count + all active session entries from Redis.</summary>
    [HttpGet("online")]
    public async Task<ActionResult> GetOnline()
    {
        var db = _redis.GetDatabase();
        var countRaw = await db.StringGetAsync("online:count");
        var count = (long?)countRaw ?? 0;

        var entries = await db.HashGetAllAsync("online:sessions");
        return Ok(new
        {
            OnlineCount = count,
            Sessions = entries.Length
        });
    }

    /// <summary>
    /// Session history from PostgreSQL.
    /// Returns up to 200 sessions ordered by ConnectedAt descending.
    /// Supports optional date range filter.
    /// </summary>
    [HttpGet("sessions")]
    public async Task<ActionResult> GetSessions(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = _db.UserSessions.AsNoTracking();

        if (from.HasValue) query = query.Where(s => s.ConnectedAt >= from.Value);
        if (to.HasValue)   query = query.Where(s => s.ConnectedAt <= to.Value);

        var rows = await query
            .OrderByDescending(s => s.ConnectedAt)
            .Take(limit)
            .Select(s => new
            {
                s.SessionId,
                s.UserId,
                s.IpAddress,
                s.ConnectedAt,
                s.DisconnectedAt,
                s.DurationSeconds
            })
            .ToListAsync();

        return Ok(rows);
    }

    /// <summary>Hourly session count for the last 24 hours (for the trend chart).</summary>
    [HttpGet("stats/hourly")]
    public async Task<ActionResult> GetHourlyStats()
    {
        var since = DateTimeOffset.UtcNow.AddHours(-24);

        var rows = await _db.UserSessions
            .AsNoTracking()
            .Where(s => s.ConnectedAt >= since)
            .Select(s => s.ConnectedAt)
            .ToListAsync();

        var hourly = rows
            .GroupBy(t => new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset))
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderBy(x => x.Hour)
            .ToList();

        return Ok(hourly);
    }
}
