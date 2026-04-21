using System.Text.RegularExpressions;
using Gateway.Services;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Minio;
using Minio.DataModel.Args;
using SBD.Messaging.Commands;

namespace Gateway.Controllers;

/// <summary>
/// SuperAdmin-facing DMC CSV import endpoints. Gateway is the only HTTP surface
/// — actual parsing lives in WorkerService and DB writes live in StudentApi.
/// </summary>
[ApiController]
[Route("api/v1/dmc-student-import")]
[Authorize]
public class DmcStudentImportController : ControllerBase
{
    private const string ImportBucket = "dmc-imports";
    private const long MaxUploadBytes = 150L * 1024 * 1024;   // 150 MB headroom over ~28 MB reality

    private readonly IMinioClient _minio;
    private readonly IDmcJobService _jobs;
    private readonly IPublishEndpoint _publish;
    private readonly ILogger<DmcStudentImportController> _logger;
    private readonly IConfiguration _config;

    public DmcStudentImportController(
        IMinioClient minio,
        IDmcJobService jobs,
        IPublishEndpoint publish,
        IConfiguration config,
        ILogger<DmcStudentImportController> logger)
    {
        _minio = minio;
        _jobs = jobs;
        _publish = publish;
        _config = config;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] short? academicYear,
        [FromForm] short? term,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "ต้องแนบไฟล์ CSV" });
        if (file.Length > MaxUploadBytes) return BadRequest(new { error = "ไฟล์เกิน 150 MB" });
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "รับเฉพาะไฟล์ .csv" });

        // Auto-detect year/term from filename like "2568-2-studentInAreaList.csv"
        var (detectedYear, detectedTerm) = DetectYearTerm(file.FileName);
        var finalYear = academicYear ?? detectedYear;
        var finalTerm = term ?? detectedTerm;
        if (finalYear is null || finalTerm is null)
            return BadRequest(new { error = "ไม่สามารถระบุปี/เทอมได้ กรุณาส่ง academicYear + term" });

        var jobId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var fileKey = $"imports/dmc/{finalYear}/{finalTerm}/{jobId}.csv";
        var bucket = _config["MinIO:ImportBucket"] ?? ImportBucket;

        // Ensure bucket exists (idempotent)
        var exists = await _minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct);
        if (!exists)
            await _minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);

        await using var stream = file.OpenReadStream();
        await _minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(fileKey)
            .WithStreamData(stream)
            .WithObjectSize(file.Length)
            .WithContentType("text/csv"), ct);

        var userId = ExtractUserId();
        await _jobs.InsertQueuedJobAsync(jobId, finalYear.Value, finalTerm.Value, fileKey, file.FileName, userId, ct);

        await _publish.Publish(new ScheduleDmcImportCommand(
            jobId, finalYear.Value, finalTerm.Value, fileKey, file.FileName, userId, correlationId), ct);

        _logger.LogInformation("DMC import queued: job={JobId} file={File} year={Year} term={Term} user={User}",
            jobId, file.FileName, finalYear, finalTerm, userId);

        return Accepted(new { jobId, correlationId, academicYear = finalYear, term = finalTerm });
    }

    [HttpDelete("{jobId:guid}")]
    public async Task<IActionResult> Cancel([FromRoute] Guid jobId, [FromQuery] string? reason, CancellationToken ct)
    {
        var userId = ExtractUserId();
        await _publish.Publish(new CancelDmcImportCommand(
            jobId, userId, reason ?? "user-cancelled", Guid.NewGuid()), ct);
        _logger.LogWarning("DMC cancel: job={JobId} by user={User} reason={Reason}", jobId, userId, reason);
        return NoContent();
    }

    private static (short? year, short? term) DetectYearTerm(string filename)
    {
        var m = Regex.Match(filename, @"(\d{4})-(\d)-");
        if (!m.Success) return (null, null);
        return (short.Parse(m.Groups[1].Value), short.Parse(m.Groups[2].Value));
    }

    private long ExtractUserId()
    {
        var sub = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(sub, out var id) ? id : 0;
    }
}
