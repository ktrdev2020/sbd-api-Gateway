using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Gateway.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #27 — Teacher dashboard aggregation endpoints (read-only, JWT-derived).
/// Three split endpoints by cache volatility:
///   /dashboard — summary + KPIs (60s cache OK)
///   /classes   — homeroom assignments + student counts (5min cache OK)
///   /tasks     — cross-module pending counters (X-No-Cache, real-time)
/// </summary>
[ApiController]
[Route("api/v1/me/personnel")]
[Authorize]
public class MeTeacherDashboardController : ControllerBase
{
    private readonly GatewayDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MeTeacherDashboardController> _logger;

    public MeTeacherDashboardController(
        SbdDbContext db, IHttpClientFactory httpFactory,
        IConfiguration config, ILogger<MeTeacherDashboardController> logger)
    {
        _db = (GatewayDbContext)db;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private int? CurrentUserId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value, out var id) ? id : null;

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    /// <summary>Resolve current user → Personnel.Id + primary SchoolCode + SmisCode.</summary>
    private async Task<(int personnelId, string? schoolCode, string? smisCode, int? schoolIdInt)?> ResolvePrimaryAsync(int userId, CancellationToken ct)
    {
        var row = await (
            from p in _db.Personnel.AsNoTracking()
            where p.UserId == userId && p.TrashedAt == null
            join psa in _db.PersonnelSchoolAssignments.AsNoTracking()
                on new { pid = p.Id, primary = true } equals new { pid = psa.PersonnelId, primary = psa.IsPrimary } into psaJ
            from psa in psaJ.DefaultIfEmpty()
            join s in _db.Schools.AsNoTracking() on psa.SchoolCode equals s.SchoolCode into sJ
            from s in sJ.DefaultIfEmpty()
            select new { p.Id, SchoolCode = psa != null ? psa.SchoolCode : null, SmisCode = s != null ? s.SmisCode : null }
        ).FirstOrDefaultAsync(ct);

        if (row == null) return null;
        var schoolIdInt = int.TryParse(row.SchoolCode, out var sid) ? sid : (int?)null;
        return (row.Id, row.SchoolCode, row.SmisCode, schoolIdInt);
    }

    public record DashboardDto(
        int PersonnelId, string FirstName, string LastName, string? Photo, string? CoverPhoto,
        string? PersonnelTypeName, string? SubjectAreaName, string? Position,
        string? SchoolCode, string? SchoolNameTh, string? Principal,
        int? SchoolSizeStd7, int? StudentCount, int? TeacherCount,
        short AcademicYear, short Term);

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> GetDashboard(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        var dto = await (
            from p in _db.Personnel.AsNoTracking()
            where p.UserId == userId && p.TrashedAt == null
            join psa in _db.PersonnelSchoolAssignments.AsNoTracking()
                on new { pid = p.Id, primary = true } equals new { pid = psa.PersonnelId, primary = psa.IsPrimary } into psaJ
            from psa in psaJ.DefaultIfEmpty()
            join s in _db.Schools.AsNoTracking() on psa.SchoolCode equals s.SchoolCode into sJ
            from s in sJ.DefaultIfEmpty()
            join pt in _db.PersonnelTypes.AsNoTracking() on p.PersonnelTypeId equals pt.Id into ptJ
            from pt in ptJ.DefaultIfEmpty()
            join sa in _db.SubjectAreas.AsNoTracking() on p.SubjectAreaId equals sa.Id into saJ
            from sa in saJ.DefaultIfEmpty()
            select new DashboardDto(
                p.Id, p.FirstName, p.LastName, p.Photo,
                EF.Property<string?>(p, "CoverPhoto"),
                pt != null ? pt.NameTh : null,
                sa != null ? sa.NameTh : null,
                psa != null ? psa.Position : null,
                psa != null ? psa.SchoolCode : null,
                s != null ? s.NameTh : null,
                s != null ? s.Principal : null,
                s != null ? s.SchoolSizeStd7 : null,
                s != null ? s.StudentCount : null,
                s != null ? s.TeacherCount : null,
                CurrentAcademicYear(), CurrentTerm())
        ).FirstOrDefaultAsync(ct);

        if (dto == null) return NoContent();
        return Ok(dto);
    }

    public record ClassroomDto(
        long? AssignmentId, long GradeLevelId, string? GradeName, short ClassroomNumber,
        int StudentCount, bool IsHomeroom, string[] AdvisorNames);

    [HttpGet("classes")]
    public async Task<ActionResult<IReadOnlyList<ClassroomDto>>> GetClasses(
        [FromQuery] short? academicYear, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        var primary = await ResolvePrimaryAsync(userId.Value, ct);
        if (primary is null || primary.Value.schoolCode is null) return Ok(Array.Empty<ClassroomDto>());

        var year = academicYear ?? CurrentAcademicYear();
        var schoolCode = primary.Value.schoolCode;
        var personnelId = primary.Value.personnelId;

        // All advisor rows for this school × year (we need them for AdvisorNames per classroom)
        var allRows = await (
            from a in _db.TeacherHomeroomAssignments.AsNoTracking()
            join p in _db.Personnel.AsNoTracking() on a.PersonnelId equals p.Id
            where a.SchoolCode == schoolCode && a.AcademicYear == year && a.DeletedAt == null
            select new { a.Id, a.PersonnelId, a.GradeLevelId, a.ClassroomNumber, p.FirstName, p.LastName }
        ).ToListAsync(ct);

        // Group by classroom; pick out rows for THIS teacher
        var myKeys = allRows.Where(r => r.PersonnelId == personnelId)
            .Select(r => (r.GradeLevelId, r.ClassroomNumber)).ToHashSet();

        // Student counts via StudentApi (SmisCode)
        var smis = primary.Value.smisCode ?? schoolCode;
        var counts = await FetchClassroomCountsAsync(smis, year, ct);

        // Compose rows for classrooms where this teacher is advisor
        var rows = allRows
            .Where(r => myKeys.Contains((r.GradeLevelId, r.ClassroomNumber)))
            .GroupBy(r => (r.GradeLevelId, r.ClassroomNumber))
            .Select(g =>
            {
                var key = $"{g.Key.GradeLevelId}|{g.Key.ClassroomNumber}";
                counts.TryGetValue(key, out var c);
                var first = g.First();
                return new ClassroomDto(
                    AssignmentId: g.FirstOrDefault(x => x.PersonnelId == personnelId)?.Id,
                    GradeLevelId: g.Key.GradeLevelId,
                    GradeName: c.gradeName,
                    ClassroomNumber: g.Key.ClassroomNumber,
                    StudentCount: c.count,
                    IsHomeroom: true,
                    AdvisorNames: g.Select(x => $"{x.FirstName} {x.LastName}").ToArray());
            })
            .OrderBy(r => r.GradeLevelId).ThenBy(r => r.ClassroomNumber)
            .ToList();

        return Ok(rows);
    }

    public record TasksDto(
        int SssOpenCases, int EdmUnread, int SsmsUpcoming,
        int AplanPendingMine, int CurriculumDraftMine, int Total);

    [HttpGet("tasks")]
    public ActionResult<TasksDto> GetTasks(CancellationToken ct)
    {
        // X-No-Cache hint to client (HTTP cache interceptor reads this)
        Response.Headers["X-No-Cache"] = "1";

        // Phase 1 stub: return zeros. T2.x will wire to each module's
        // pending-count endpoint as those endpoints land. Frontend already
        // handles zero-counts gracefully (badge hidden).
        return Ok(new TasksDto(0, 0, 0, 0, 0, 0));
    }

    private async Task<Dictionary<string, (int count, string? gradeName)>> FetchClassroomCountsAsync(
        string smisCode, short year, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(8);
        var url = $"{StudentApiBase}/api/v1/school/{smisCode}/classrooms?academicYear={year}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        var token = HttpContext.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(token) && token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            req.Headers.Authorization = AuthenticationHeaderValue.Parse(token);

        var dict = new Dictionary<string, (int count, string? gradeName)>();
        try
        {
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return dict;
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return dict;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("gradeLevelId", out var gid)) continue;
                if (!el.TryGetProperty("classroomNumber", out var cn)) continue;
                var count = el.TryGetProperty("studentCount", out var sc) ? sc.GetInt32() : 0;
                string? name = el.TryGetProperty("gradeName", out var gn) ? gn.GetString() : null;
                dict[$"{gid.GetInt64()}|{cn.GetInt16()}"] = (count, name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MeTeacherDashboard] StudentApi classroom counts unavailable for {Smis}/{Year}", smisCode, year);
        }
        return dict;
    }

    public record PhotoUpdateResponse(string Photo);
    public record CoverUpdateResponse(string CoverPhoto);

    /// <summary>POST /api/v1/me/personnel/photo — upload avatar (multipart). Forwards file to FileService, then updates Personnel.Photo URL.</summary>
    [HttpPost("photo")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<PhotoUpdateResponse>> UploadPhoto(CancellationToken ct)
    {
        var url = await UploadAndGetUrlAsync("avatar", ct);
        if (url is null) return BadRequest(new { message = "Upload failed" });

        var userId = CurrentUserId!.Value;
        var personnel = await _db.Personnel.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (personnel == null) return NotFound();
        personnel.Photo = url;
        await _db.SaveChangesAsync(ct);
        return Ok(new PhotoUpdateResponse(url));
    }

    /// <summary>POST /api/v1/me/personnel/cover — upload cover photo (multipart). Updates Personnel.CoverPhoto shadow property.</summary>
    [HttpPost("cover")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<CoverUpdateResponse>> UploadCover(CancellationToken ct)
    {
        var url = await UploadAndGetUrlAsync("cover", ct);
        if (url is null) return BadRequest(new { message = "Upload failed" });

        var userId = CurrentUserId!.Value;
        var personnel = await _db.Personnel.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (personnel == null) return NotFound();
        _db.Entry(personnel).Property("CoverPhoto").CurrentValue = url;
        await _db.SaveChangesAsync(ct);
        return Ok(new CoverUpdateResponse(url));
    }

    private async Task<string?> UploadAndGetUrlAsync(string kind, CancellationToken ct)
    {
        if (!Request.HasFormContentType) return null;
        var form = await Request.ReadFormAsync(ct);
        if (form.Files.Count == 0) return null;

        var fileServiceBase = _config["Services:FileService:BaseUrl"]
            ?? Environment.GetEnvironmentVariable("FILE_SERVICE_URL")
            ?? "http://localhost:5060";

        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(2);

        using var content = new MultipartFormDataContent($"----sbd-{Guid.NewGuid():N}");
        content.Add(new StringContent("personnel"), "ownerType");
        content.Add(new StringContent(CurrentUserId!.Value.ToString()), "ownerId");
        content.Add(new StringContent(kind), "kind");

        var file = form.Files[0];
        var streamContent = new StreamContent(file.OpenReadStream());
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        content.Add(streamContent, "file", file.FileName);

        var req = new HttpRequestMessage(HttpMethod.Post, $"{fileServiceBase}/api/v1/files") { Content = content };
        var auth = HttpContext.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            req.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);

        try
        {
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("url", out var u)) return u.GetString();
            if (doc.RootElement.TryGetProperty("publicUrl", out var pu)) return pu.GetString();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MeTeacherDashboard] Photo upload to FileService failed (kind={Kind})", kind);
            return null;
        }
    }

    private static short CurrentAcademicYear()
    {
        var now = DateTime.Now;
        var beYear = now.Year + 543;
        // Thai academic year flips in May (M1 starts mid-May)
        return (short)(now.Month >= 5 ? beYear : beYear - 1);
    }

    private static short CurrentTerm()
    {
        var m = DateTime.Now.Month;
        return (short)(m >= 5 && m <= 10 ? 1 : 2);
    }
}
