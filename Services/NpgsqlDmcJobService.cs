using Npgsql;

namespace Gateway.Services;

public class NpgsqlDmcJobService : IDmcJobService
{
    private readonly string _connectionString;

    public NpgsqlDmcJobService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("StudentDb")
            ?? throw new InvalidOperationException("StudentDb connection string not configured for Gateway");
    }

    public async Task InsertQueuedJobAsync(
        Guid jobId,
        short academicYear,
        short term,
        string sourceFileKey,
        string sourceFileName,
        long createdByUserId,
        CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO dmc_import_jobs
                (id, academic_year, term, source_file_key, source_file_name,
                 processed_rows, succeeded_rows, failed_rows,
                 status, created_at, created_by)
            VALUES (@id, @year, @term, @key, @name, 0, 0, 0, 'queued', now(), @creator)",
            conn);
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue("@year", academicYear);
        cmd.Parameters.AddWithValue("@term", term);
        cmd.Parameters.AddWithValue("@key", sourceFileKey);
        cmd.Parameters.AddWithValue("@name", sourceFileName);
        cmd.Parameters.AddWithValue("@creator", createdByUserId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
