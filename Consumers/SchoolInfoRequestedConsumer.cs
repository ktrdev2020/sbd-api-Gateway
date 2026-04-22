using MassTransit;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using SBD.Messaging.Events;

namespace Gateway.Consumers;

/// <summary>
/// Answers cross-DB school lookups from StudentApi. Publishes
/// SchoolInfoResponseEvent on hit, SchoolInfoNotFoundEvent on miss.
/// </summary>
public class SchoolInfoRequestedConsumer : IConsumer<SchoolInfoRequestedEvent>
{
    private readonly SbdDbContext _db;
    private readonly IPublishEndpoint _publish;
    private readonly ILogger<SchoolInfoRequestedConsumer> _logger;

    public SchoolInfoRequestedConsumer(SbdDbContext db, IPublishEndpoint publish, ILogger<SchoolInfoRequestedConsumer> logger)
    {
        _db = db;
        _publish = publish;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SchoolInfoRequestedEvent> context)
    {
        var req = context.Message;
        var school = await _db.Schools
            .AsNoTracking()
            .Where(s => (s.SchoolCode == req.SchoolCode || s.SmisCode == req.SchoolCode) && s.DeletedAt == null)
            .Select(s => new { s.SchoolCode, s.NameTh, s.NameEn, s.AreaId, s.SchoolLevelId, s.Principal, s.IsActive })
            .FirstOrDefaultAsync(context.CancellationToken);

        if (school is null)
        {
            _logger.LogWarning("School lookup miss: {Code} from {Service}", req.SchoolCode, req.RequestingService);
            await _publish.Publish(new SchoolInfoNotFoundEvent(
                req.CorrelationId, req.SchoolCode, DateTimeOffset.UtcNow),
                context.CancellationToken);
            return;
        }

        // Gateway's Schools table uses string SchoolCode as PK (no numeric SchoolId).
        // For transport we synthesize a stable numeric id from the hash of SchoolCode —
        // StudentApi stores it as CachedSchoolId but does not treat it as an FK into Gateway.
        var syntheticId = Math.Abs((long)req.SchoolCode.GetHashCode());

        await _publish.Publish(new SchoolInfoResponseEvent(
            req.CorrelationId,
            req.SchoolCode,
            school.SchoolCode,
            syntheticId,
            school.NameTh,
            school.NameEn,
            school.AreaId,
            school.SchoolLevelId,
            school.Principal,
            school.IsActive,
            DateTimeOffset.UtcNow),
            context.CancellationToken);

        _logger.LogDebug("Resolved school {Code} for {Service}", req.SchoolCode, req.RequestingService);
    }
}
