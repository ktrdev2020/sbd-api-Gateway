namespace Gateway.Services;

/// <summary>
/// Thin wrapper over Npgsql to write DMC job rows into the student-db.
/// Gateway does not own StudentDbContext (separate bounded context), so we
/// use raw SQL against the known schema for the small set of write-side
/// operations this controller performs.
/// </summary>
public interface IDmcJobService
{
    Task InsertQueuedJobAsync(
        Guid jobId,
        short academicYear,
        short term,
        string sourceFileKey,
        string sourceFileName,
        long createdByUserId,
        CancellationToken ct);
}
