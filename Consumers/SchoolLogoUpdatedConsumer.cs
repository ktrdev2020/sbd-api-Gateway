using MassTransit;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using SBD.Messaging.Events;

namespace Gateway.Consumers;

/// <summary>
/// Listens for <see cref="SchoolLogoUpdatedEvent"/> published by FileService and
/// keeps the denormalized <c>Schools.LogoUrl</c>, <c>LogoThumbnailUrl</c>, and
/// <c>LogoVersion</c> columns in sync. The version stamp is appended to the
/// stored URL as a query string so browser/CDN caches naturally invalidate
/// when a new logo is uploaded.
///
/// Why denormalize: callers of <c>GET /api/v1/school/{id}</c> can render the
/// logo immediately without an extra hop to FileService. The actual file lives
/// only in MinIO + the Files table; the columns here are just a hot cache.
/// </summary>
public class SchoolLogoUpdatedConsumer : IConsumer<SchoolLogoUpdatedEvent>
{
    private readonly SbdDbContext _context;
    private readonly ILogger<SchoolLogoUpdatedConsumer> _logger;

    public SchoolLogoUpdatedConsumer(SbdDbContext context, ILogger<SchoolLogoUpdatedConsumer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SchoolLogoUpdatedEvent> context)
    {
        var msg = context.Message;
        var school = await _context.Schools.AsTracking()
            .FirstOrDefaultAsync(s => s.SchoolCode == msg.SchoolCode, context.CancellationToken);

        if (school == null)
        {
            _logger.LogWarning(
                "[SchoolLogoUpdated] School {SchoolCode} not found — dropping event",
                msg.SchoolCode);
            return;
        }

        // Append version as a cache buster — browsers will refetch when it changes.
        var sep = msg.MainUrl.Contains('?') ? '&' : '?';
        school.LogoUrl = $"{msg.MainUrl}{sep}v={msg.Version}";
        school.LogoThumbnailUrl = $"{msg.ThumbnailUrl}{sep}v={msg.Version}";
        school.LogoVersion = msg.Version;

        await _context.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "[SchoolLogoUpdated] School {SchoolCode} → version {Version}",
            msg.SchoolCode, msg.Version);
    }
}
