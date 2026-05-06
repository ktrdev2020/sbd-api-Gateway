using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Gateway.Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using Gateway.Services;

namespace Gateway.Controllers;

/// <summary>
/// Plan #27 Phase A.0 — Homeroom advisor assignment per classroom × academic year.
/// M:N mapping: 1 classroom can have many advisor teachers, 1 teacher can advise
/// many classrooms. Used by Teacher dashboard "ห้องที่ดูแล".
/// </summary>
[ApiController]
[Route("api/v1/schools/{schoolCode}/homeroom-assignments")]
[Authorize]
public class SchoolHomeroomController : ControllerBase
{
    private readonly GatewayDbContext _db;
    private readonly ICapabilityService _capabilities;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SchoolHomeroomController> _logger;

    public SchoolHomeroomController(
        SbdDbContext db, ICapabilityService capabilities,
        IHttpClientFactory httpFactory, IConfiguration config,
        ILogger<SchoolHomeroomController> logger)
    {
        _db = (GatewayDbContext)db;
        _capabilities = capabilities;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private string StudentApiBase =>
        _config["ServiceUrls:StudentApi"]
        ?? Environment.GetEnvironmentVariable("STUDENT_API_URL")
        ?? "http://localhost:5032";

    private int? CurrentUserId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : null;

    private long CapVersion =>
        long.TryParse(User.FindFirstValue("cap_v"), out var v) ? v : 0L;

    /// <summary>
    /// Authorize mutation. Allowed: SuperAdmin · SchoolAdmin scoped to this school ·
    /// holder of `school:homeroom:assign` capability scoped to this school.
    /// </summary>
    private async Task<bool> CanAssignAsync(string schoolCode, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return false;

        if (User.IsInRole("super_admin") || User.IsInRole("SuperAdmin")) return true;

        var scopeId = int.TryParse(schoolCode, out var sid) ? sid : (int?)null;

        // SchoolAdmin scoped to this school?
        if (scopeId.HasValue)
        {
            var hasSchoolAdmin = await _db.UserRoles.AsNoTracking()
                .Include(ur => ur.Role)
                .AnyAsync(ur => ur.UserId == userId
                    && ur.ScopeType == "School" && ur.ScopeId == scopeId
                    && (ur.Role!.Code == "school_admin" || ur.Role!.Code == "SchoolAdmin"), ct);
            if (hasSchoolAdmin) return true;
        }

        // Capability grant?
        return await _capabilities.HasCapabilityAsync(
            userId.Value, CapVersion, "school:homeroom:assign", "school", scopeId, ct);
    }

    public record HomeroomAssignmentDto(
        long Id, int PersonnelId, string FirstName, string LastName, string? Photo,
        string SchoolCode, short AcademicYear, short? Term,
        long GradeLevelId, short ClassroomNumber, string Role,
        int AssignedByUserId, DateTimeOffset AssignedAt, DateOnly? EndDate);

    public record AssignRequest(
        int[] PersonnelIds, long GradeLevelId, short ClassroomNumber,
        short AcademicYear, short? Term);

    /// <summary>
    /// List all advisor assignments for a school × academic year.
    /// Joined with Personnel for display (FirstName, LastName, Photo).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HomeroomAssignmentDto>>> List(
        string schoolCode, [FromQuery] short academicYear, [FromQuery] short? term, CancellationToken ct)
    {
        var rows = await (
            from a in _db.TeacherHomeroomAssignments.AsNoTracking()
            join p in _db.Personnel.AsNoTracking() on a.PersonnelId equals p.Id
            where a.SchoolCode == schoolCode
                  && a.AcademicYear == academicYear
                  && (term == null || a.Term == term)
                  && a.DeletedAt == null
            orderby a.GradeLevelId, a.ClassroomNumber, p.FirstName
            select new HomeroomAssignmentDto(
                a.Id, p.Id, p.FirstName, p.LastName, p.Photo,
                a.SchoolCode, a.AcademicYear, a.Term,
                a.GradeLevelId, a.ClassroomNumber, a.Role,
                a.AssignedByUserId, a.AssignedAt, a.EndDate)
        ).ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Bulk assign teachers as advisors of a single classroom for an academic year.
    /// Replaces existing assignments for that (school, year, term, grade, room) tuple
    /// with the new list (additions inserted, removals soft-deleted).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<HomeroomAssignmentDto>>> Assign(
        string schoolCode, [FromBody] AssignRequest req, CancellationToken ct)
    {
        if (!await CanAssignAsync(schoolCode, ct)) return Forbid();
        var userId = CurrentUserId!.Value;

        var existing = await _db.TeacherHomeroomAssignments
            .Where(a => a.SchoolCode == schoolCode
                        && a.AcademicYear == req.AcademicYear
                        && a.Term == req.Term
                        && a.GradeLevelId == req.GradeLevelId
                        && a.ClassroomNumber == req.ClassroomNumber
                        && a.DeletedAt == null)
            .ToListAsync(ct);

        var keepSet = req.PersonnelIds.ToHashSet();
        var existingSet = existing.Select(e => e.PersonnelId).ToHashSet();

        // Soft-delete removed
        foreach (var row in existing.Where(e => !keepSet.Contains(e.PersonnelId)))
        {
            row.DeletedAt = DateTimeOffset.UtcNow;
            row.DeletedByUserId = userId;
        }

        // Insert added
        foreach (var pid in req.PersonnelIds.Where(p => !existingSet.Contains(p)))
        {
            _db.TeacherHomeroomAssignments.Add(new TeacherHomeroomAssignment
            {
                PersonnelId = pid,
                SchoolCode = schoolCode,
                AcademicYear = req.AcademicYear,
                Term = req.Term,
                GradeLevelId = req.GradeLevelId,
                ClassroomNumber = req.ClassroomNumber,
                Role = "advisor",
                AssignedByUserId = userId,
                AssignedAt = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);

        return await List(schoolCode, req.AcademicYear, req.Term, ct);
    }

    public record TeacherPickDto(int PersonnelId, string FirstName, string LastName, string? Photo, string? Position, int? PersonnelTypeId);
    public record ClassroomDto(long GradeLevelId, string? GradeName, int LevelOrder, short ClassroomNumber, int StudentCount);
    public record SetupDto(
        IReadOnlyList<ClassroomDto> Classrooms,
        IReadOnlyList<TeacherPickDto> Teachers,
        IReadOnlyList<HomeroomAssignmentDto> Assignments);

    /// <summary>
    /// One-shot bootstrap for the SchoolAdmin assignment grid.
    /// Returns classrooms (from StudentApi), teachers in this school
    /// (from Gateway), and current assignments. Reduces UI from 3 calls to 1.
    /// </summary>
    [HttpGet("setup")]
    public async Task<ActionResult<SetupDto>> Setup(
        string schoolCode, [FromQuery] short academicYear, CancellationToken ct)
    {
        var smis = await _db.Schools.AsNoTracking()
            .Where(s => s.SchoolCode == schoolCode)
            .Select(s => s.SmisCode ?? schoolCode)
            .FirstOrDefaultAsync(ct) ?? schoolCode;

        // StudentApi call is independent of _db so it runs in parallel; the two
        // EF queries share the same DbContext and must run sequentially
        // (DbContext is NOT thread-safe).
        var classroomsTask = FetchClassroomsAsync(smis, academicYear, ct);

        var teachers = await (
            from p in _db.Personnel.AsNoTracking()
            join psa in _db.PersonnelSchoolAssignments.AsNoTracking()
                on p.Id equals psa.PersonnelId
            where psa.SchoolCode == schoolCode && psa.IsPrimary && p.TrashedAt == null
            orderby p.FirstName
            select new TeacherPickDto(p.Id, p.FirstName, p.LastName, p.Photo, psa.Position, p.PersonnelTypeId)
        ).ToListAsync(ct);

        var assignments = await (
            from a in _db.TeacherHomeroomAssignments.AsNoTracking()
            join p in _db.Personnel.AsNoTracking() on a.PersonnelId equals p.Id
            where a.SchoolCode == schoolCode && a.AcademicYear == academicYear && a.DeletedAt == null
            orderby a.GradeLevelId, a.ClassroomNumber, p.FirstName
            select new HomeroomAssignmentDto(
                a.Id, p.Id, p.FirstName, p.LastName, p.Photo,
                a.SchoolCode, a.AcademicYear, a.Term,
                a.GradeLevelId, a.ClassroomNumber, a.Role,
                a.AssignedByUserId, a.AssignedAt, a.EndDate)
        ).ToListAsync(ct);

        var classrooms = await classroomsTask;

        return Ok(new SetupDto(classrooms, teachers, assignments));
    }

    private async Task<IReadOnlyList<ClassroomDto>> FetchClassroomsAsync(string smis, short year, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(8);
        var url = $"{StudentApiBase}/api/v1/school/{smis}/classrooms?academicYear={year}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        var auth = HttpContext.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            req.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);

        var list = new List<ClassroomDto>();
        try
        {
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return list;
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var gid = el.TryGetProperty("gradeLevelId", out var g) ? g.GetInt64() : 0L;
                var gname = el.TryGetProperty("gradeName", out var gn) ? gn.GetString() : null;
                var ord = el.TryGetProperty("levelOrder", out var lo) ? lo.GetInt32() : 0;
                var cn = el.TryGetProperty("classroomNumber", out var c) ? c.GetInt16() : (short)0;
                var sc = el.TryGetProperty("studentCount", out var s) ? s.GetInt32() : 0;
                list.Add(new ClassroomDto(gid, gname, ord, cn, sc));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SchoolHomeroom] StudentApi classrooms unavailable for {Smis}/{Year}", smis, year);
        }
        return list;
    }

    /// <summary>
    /// Soft-delete a single assignment.
    /// </summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Remove(string schoolCode, long id, CancellationToken ct)
    {
        if (!await CanAssignAsync(schoolCode, ct)) return Forbid();

        var row = await _db.TeacherHomeroomAssignments
            .FirstOrDefaultAsync(a => a.Id == id && a.SchoolCode == schoolCode && a.DeletedAt == null, ct);
        if (row == null) return NotFound();

        row.DeletedAt = DateTimeOffset.UtcNow;
        row.DeletedByUserId = CurrentUserId;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
