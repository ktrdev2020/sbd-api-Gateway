using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #101 — Public guest endpoints for the /academic-info portal page.
/// Serves O-NET school-level results (ผลรายโรง แยกรายวิชา) imported from NIETS
/// CSVs into <c>OnetSchoolResults</c>. All actions anonymous, response-cached 1h.
///
/// <c>OnetSchoolResults</c> is intentionally NOT an EF entity — it is a
/// Gateway-local reporting table (created in Program.cs seed, filled by
/// tools/import-onet.mjs), queried via <c>Database.SqlQuery&lt;T&gt;</c> so no
/// SBD.Domain/Infrastructure NuGet round-trip is needed.
///
/// Route-per-grade (no query-string variance) because Gateway does not run
/// ResponseCachingMiddleware — edge caches vary per-URL only.
/// </summary>
[ApiController]
[Route("api/v1/guest/academic-info")]
[AllowAnonymous]
public class GuestAcademicInfoController : ControllerBase
{
    private const int CacheSeconds = 3600;
    private static readonly string[] Grades = ["P6", "M3", "M6"];

    private readonly SbdDbContext _context;

    public GuestAcademicInfoController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>Latest imported education year (พ.ศ.), or null when no data.</summary>
    private async Task<int?> LatestYearAsync(CancellationToken ct) =>
        await _context.Database
            .SqlQuery<int?>($"SELECT MAX(\"EducationYear\") AS \"Value\" FROM \"OnetSchoolResults\"")
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// O-NET overview: per grade level → per subject area aggregates
    /// (weighted mean by test takers) for the latest education year.
    /// </summary>
    [HttpGet("summary")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestOnetSummaryDto>> GetSummary(CancellationToken ct)
    {
        var year = await LatestYearAsync(ct);
        if (year is null)
            return Ok(new GuestOnetSummaryDto(0, []));

        var rows = await _context.Database.SqlQuery<OnetSubjectAggRow>($"""
            SELECT "GradeLevel"      AS "Grade",
                   "Subject"         AS "Subject",
                   COUNT(*)::int     AS "SchoolCount",
                   SUM("TestTakers")::int AS "TestTakers",
                   ROUND(SUM("MeanScore" * "TestTakers") / NULLIF(SUM("TestTakers"), 0), 2) AS "AreaMean",
                   MAX("MeanScore")  AS "MaxSchoolMean",
                   MIN("MeanScore")  AS "MinSchoolMean",
                   SUM("CountAboveHalf")::int AS "CountAboveHalf"
            FROM "OnetSchoolResults"
            WHERE "EducationYear" = {year}
            GROUP BY "GradeLevel", "Subject"
            """).ToListAsync(ct);

        var grades = Grades
            .Select(g =>
            {
                var subjects = rows
                    .Where(r => r.Grade == g)
                    .OrderBy(r => SubjectOrder(r.Subject))
                    .Select(r => new GuestOnetSubjectDto(
                        r.Subject, r.SchoolCount, r.TestTakers, r.AreaMean,
                        r.MaxSchoolMean, r.MinSchoolMean, r.CountAboveHalf))
                    .ToList();
                return new GuestOnetGradeDto(
                    g,
                    subjects.Count == 0 ? 0 : subjects.Max(s => s.SchoolCount),
                    subjects.Count == 0 ? 0 : subjects.Max(s => s.TestTakers),
                    subjects.Count == 0 ? null : Math.Round(subjects.Average(s => s.AreaMean ?? 0), 2),
                    subjects);
            })
            .Where(g => g.Subjects.Count > 0)
            .ToList();

        return Ok(new GuestOnetSummaryDto(year.Value, grades));
    }

    /// <summary>
    /// Per-school O-NET results for one grade level (latest year), pivoted to
    /// one row per school with per-subject mean scores, ordered by overall mean.
    /// </summary>
    [HttpGet("onet/schools/{grade}")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestOnetSchoolsDto>> GetSchools(string grade, CancellationToken ct)
    {
        var g = grade.ToUpperInvariant();
        if (!Grades.Contains(g))
            return NotFound(new { message = $"unknown grade '{grade}' — expected P6|M3|M6" });

        var year = await LatestYearAsync(ct);
        if (year is null)
            return Ok(new GuestOnetSchoolsDto(0, g, []));

        var rows = await _context.Database.SqlQuery<OnetSchoolRow>($"""
            SELECT o."SmisCode"   AS "SmisCode",
                   s."NameTh"     AS "SchoolName",
                   o."Subject"    AS "Subject",
                   o."TestTakers" AS "TestTakers",
                   o."MeanScore"  AS "MeanScore"
            FROM "OnetSchoolResults" o
            JOIN "Schools" s ON s."SmisCode" = o."SmisCode" AND s."DeletedAt" IS NULL
            WHERE o."EducationYear" = {year} AND o."GradeLevel" = {g}
            """).ToListAsync(ct);

        var schools = rows
            .GroupBy(r => (r.SmisCode, r.SchoolName))
            .Select(grp => new GuestOnetSchoolDto(
                grp.Key.SmisCode,
                grp.Key.SchoolName,
                grp.Max(r => r.TestTakers),
                Math.Round(grp.Average(r => r.MeanScore), 2),
                grp.OrderBy(r => SubjectOrder(r.Subject))
                   .Select(r => new GuestOnetSchoolSubjectDto(r.Subject, r.TestTakers, r.MeanScore))
                   .ToList()))
            .OrderByDescending(sc => sc.OverallMean)
            .ToList();

        return Ok(new GuestOnetSchoolsDto(year.Value, g, schools));
    }

    private static int SubjectOrder(string subject) => subject switch
    {
        "thai" => 1,
        "math" => 2,
        "science" => 3,
        "english" => 4,
        "social" => 5,
        _ => 9,
    };
}

// ── SqlQuery row shapes (property names must match SQL aliases) ──────────────

internal sealed class OnetSubjectAggRow
{
    public string Grade { get; set; } = "";
    public string Subject { get; set; } = "";
    public int SchoolCount { get; set; }
    public int TestTakers { get; set; }
    public decimal? AreaMean { get; set; }
    public decimal? MaxSchoolMean { get; set; }
    public decimal? MinSchoolMean { get; set; }
    public int? CountAboveHalf { get; set; }
}

internal sealed class OnetSchoolRow
{
    public string SmisCode { get; set; } = "";
    public string SchoolName { get; set; } = "";
    public string Subject { get; set; } = "";
    public int TestTakers { get; set; }
    public decimal MeanScore { get; set; }
}

// ── Response DTOs ────────────────────────────────────────────────────────────

public record GuestOnetSummaryDto(int Year, List<GuestOnetGradeDto> Grades);

public record GuestOnetGradeDto(
    string Grade,
    int SchoolCount,
    int TestTakers,
    decimal? OverallMean,
    List<GuestOnetSubjectDto> Subjects);

public record GuestOnetSubjectDto(
    string Subject,
    int SchoolCount,
    int TestTakers,
    decimal? AreaMean,
    decimal? MaxSchoolMean,
    decimal? MinSchoolMean,
    int? CountAboveHalf);

public record GuestOnetSchoolsDto(int Year, string Grade, List<GuestOnetSchoolDto> Schools);

public record GuestOnetSchoolDto(
    string SmisCode,
    string SchoolName,
    int TestTakers,
    decimal OverallMean,
    List<GuestOnetSchoolSubjectDto> Subjects);

public record GuestOnetSchoolSubjectDto(string Subject, int TestTakers, decimal MeanScore);
