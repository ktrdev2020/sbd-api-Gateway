using MassTransit;
using SBD.Application.Interfaces;
using SBD.Messaging.Events;

namespace Gateway.Consumers;

/// <summary>
/// Listens for <see cref="CacheInvalidateEvent"/> published by any service and
/// removes the affected keys from the Gateway Redis cache.
///
/// When <see cref="CacheInvalidateEvent.Pattern"/> is set, all keys matching the
/// glob pattern are removed (e.g. "refdata:schools*").
/// Otherwise the exact <see cref="CacheInvalidateEvent.CacheKey"/> is removed.
/// </summary>
public class CacheInvalidateConsumer(ICacheService cache, ILogger<CacheInvalidateConsumer> logger)
    : IConsumer<CacheInvalidateEvent>
{
    public async Task Consume(ConsumeContext<CacheInvalidateEvent> context)
    {
        var msg = context.Message;

        if (!string.IsNullOrEmpty(msg.Pattern))
        {
            logger.LogInformation("[CacheInvalidate] pattern={Pattern}", msg.Pattern);
            await cache.RemoveByPatternAsync(msg.Pattern, context.CancellationToken);
        }
        else
        {
            logger.LogInformation("[CacheInvalidate] key={Key}", msg.CacheKey);
            await cache.RemoveAsync(msg.CacheKey, context.CancellationToken);
        }
    }
}
