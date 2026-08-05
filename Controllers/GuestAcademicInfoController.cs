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

    // ═════════════════════════════ Plan #102 — NT ═══════════════════════════

    private static readonly string[] QrGrades =
        ["P1", "P2", "P3", "P4", "P5", "P6", "M1", "M2", "M3", "M4", "M5", "M6"];

    private async Task<int?> LatestYearOfAsync(string table, CancellationToken ct) =>
        table switch
        {
            "nt" => await _context.Database.SqlQuery<int?>(
                $"SELECT MAX(\"EducationYear\") AS \"Value\" FROM \"NtSchoolResults\"").FirstOrDefaultAsync(ct),
            "rt" => await _context.Database.SqlQuery<int?>(
                $"SELECT MAX(\"EducationYear\") AS \"Value\" FROM \"RtSchoolResults\"").FirstOrDefaultAsync(ct),
            "qr" => await _context.Database.SqlQuery<int?>(
                $"SELECT MAX(\"EducationYear\") AS \"Value\" FROM \"QrSchoolResults\"").FirstOrDefaultAsync(ct),
            _ => await LatestYearAsync(ct),
        };

    private async Task<List<NtRow>> NtRowsAsync(int year, CancellationToken ct) =>
        await _context.Database.SqlQuery<NtRow>($"""
            SELECT r."SmisCode"   AS "SmisCode",
                   s."NameTh"     AS "SchoolName",
                   r."SchoolSize" AS "SchoolSize",
                   q."TotalStudents" AS "StudentCount",
                   r."MathScore"  AS "MathScore",  r."MathLevel"  AS "MathLevel",
                   r."ThaiScore"  AS "ThaiScore",  r."ThaiLevel"  AS "ThaiLevel",
                   r."TotalScore" AS "TotalScore", r."TotalLevel" AS "TotalLevel"
            FROM "NtSchoolResults" r
            JOIN "Schools" s ON s."SmisCode" = r."SmisCode" AND s."DeletedAt" IS NULL
            LEFT JOIN "QrSchoolResults" q
                   ON q."SmisCode" = r."SmisCode"
                  AND q."EducationYear" = r."EducationYear"
                  AND q."GradeLevel" = 'P3'
            WHERE r."EducationYear" = {year}
            """).ToListAsync(ct);

    /// <summary>NT ป.3 area summary: averages + quality-level distributions.</summary>
    [HttpGet("nt/summary")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestNtSummaryDto>> GetNtSummary(CancellationToken ct)
    {
        var year = await LatestYearOfAsync("nt", ct);
        if (year is null) return Ok(new GuestNtSummaryDto(0, 0, null, null, null, [], [], []));

        var rows = await NtRowsAsync(year.Value, ct);
        static List<GuestLevelCountDto> Dist(IEnumerable<string?> levels) => levels
            .Where(l => !string.IsNullOrEmpty(l))
            .GroupBy(l => l!)
            .Select(g => new GuestLevelCountDto(g.Key, g.Count()))
            .OrderByDescending(d => d.Count)
            .ToList();

        return Ok(new GuestNtSummaryDto(
            year.Value, rows.Count,
            WeightedAvg(rows.Select(r => (r.MathScore, r.StudentCount))),
            WeightedAvg(rows.Select(r => (r.ThaiScore, r.StudentCount))),
            WeightedAvg(rows.Select(r => (r.TotalScore, r.StudentCount))),
            Dist(rows.Select(r => r.MathLevel)), Dist(rows.Select(r => r.ThaiLevel)), Dist(rows.Select(r => r.TotalLevel))));
    }

    /// <summary>NT per-school results, ranked by total score.</summary>
    [HttpGet("nt/schools")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestNtSchoolsDto>> GetNtSchools(CancellationToken ct)
    {
        var year = await LatestYearOfAsync("nt", ct);
        if (year is null) return Ok(new GuestNtSchoolsDto(0, []));
        var rows = await NtRowsAsync(year.Value, ct);
        return Ok(new GuestNtSchoolsDto(year.Value,
            rows.OrderByDescending(r => r.TotalScore ?? -1).ToList()));
    }

    // ═════════════════════════════ Plan #102 — RT ═══════════════════════════

    private async Task<List<RtRow>> RtRowsAsync(int year, CancellationToken ct) =>
        await _context.Database.SqlQuery<RtRow>($"""
            SELECT r."SmisCode"       AS "SmisCode",
                   s."NameTh"         AS "SchoolName",
                   r."SchoolSize"     AS "SchoolSize",
                   q."TotalStudents"  AS "StudentCount",
                   r."ReadAloudScore" AS "ReadAloudScore", r."ReadAloudPct" AS "ReadAloudPct", r."ReadAloudLevel" AS "ReadAloudLevel",
                   r."ReadCompScore"  AS "ReadCompScore",  r."ReadCompPct"  AS "ReadCompPct",  r."ReadCompLevel"  AS "ReadCompLevel",
                   r."TotalPct"       AS "TotalPct",       r."TotalLevel"   AS "TotalLevel"
            FROM "RtSchoolResults" r
            JOIN "Schools" s ON s."SmisCode" = r."SmisCode" AND s."DeletedAt" IS NULL
            LEFT JOIN "QrSchoolResults" q
                   ON q."SmisCode" = r."SmisCode"
                  AND q."EducationYear" = r."EducationYear"
                  AND q."GradeLevel" = 'P1'
            WHERE r."EducationYear" = {year}
            """).ToListAsync(ct);

    /// <summary>RT ป.1 area summary: averages + quality-level distributions.</summary>
    [HttpGet("rt/summary")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestRtSummaryDto>> GetRtSummary(CancellationToken ct)
    {
        var year = await LatestYearOfAsync("rt", ct);
        if (year is null) return Ok(new GuestRtSummaryDto(0, 0, null, null, null, [], [], []));

        var rows = await RtRowsAsync(year.Value, ct);
        static List<GuestLevelCountDto> Dist(IEnumerable<string?> levels) => levels
            .Where(l => !string.IsNullOrEmpty(l))
            .GroupBy(l => l!)
            .Select(g => new GuestLevelCountDto(g.Key, g.Count()))
            .OrderByDescending(d => d.Count)
            .ToList();

        return Ok(new GuestRtSummaryDto(
            year.Value, rows.Count,
            WeightedAvg(rows.Select(r => (r.ReadAloudPct, r.StudentCount))),
            WeightedAvg(rows.Select(r => (r.ReadCompPct, r.StudentCount))),
            WeightedAvg(rows.Select(r => (r.TotalPct, r.StudentCount))),
            Dist(rows.Select(r => r.ReadAloudLevel)), Dist(rows.Select(r => r.ReadCompLevel)), Dist(rows.Select(r => r.TotalLevel))));
    }

    /// <summary>RT per-school results, ranked by total percent.</summary>
    [HttpGet("rt/schools")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestRtSchoolsDto>> GetRtSchools(CancellationToken ct)
    {
        var year = await LatestYearOfAsync("rt", ct);
        if (year is null) return Ok(new GuestRtSchoolsDto(0, []));
        var rows = await RtRowsAsync(year.Value, ct);
        return Ok(new GuestRtSchoolsDto(year.Value,
            rows.OrderByDescending(r => r.TotalPct ?? -1).ToList()));
    }

    // ═════════════════════════════ Plan #102 — QR ═══════════════════════════

    /// <summary>คุณลักษณะอันพึงประสงค์ + อ่านคิดวิเคราะห์ฯ — per-grade aggregates.</summary>
    [HttpGet("qr/summary")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestQrSummaryDto>> GetQrSummary(CancellationToken ct)
    {
        var year = await LatestYearOfAsync("qr", ct);
        if (year is null) return Ok(new GuestQrSummaryDto(0, []));

        var rows = await _context.Database.SqlQuery<QrAggRow>($"""
            SELECT "GradeLevel" AS "Grade",
                   COUNT(*)::int AS "SchoolCount",
                   COALESCE(SUM("TotalStudents"),0)::int AS "Students",
                   COALESCE(SUM("DcPass"),0)::int AS "DcPass",
                   COALESCE(SUM("DcGood"),0)::int AS "DcGood",
                   COALESCE(SUM("DcExcellent"),0)::int AS "DcExcellent",
                   COALESCE(SUM("RcPass"),0)::int AS "RcPass",
                   COALESCE(SUM("RcGood"),0)::int AS "RcGood",
                   COALESCE(SUM("RcExcellent"),0)::int AS "RcExcellent"
            FROM "QrSchoolResults"
            WHERE "EducationYear" = {year}
            GROUP BY "GradeLevel"
            """).ToListAsync(ct);

        var grades = QrGrades
            .Select(g => rows.FirstOrDefault(r => r.Grade == g))
            .Where(r => r is not null)
            .Select(r => new GuestQrGradeDto(r!.Grade, r.SchoolCount, r.Students,
                r.DcPass, r.DcGood, r.DcExcellent, r.RcPass, r.RcGood, r.RcExcellent))
            .ToList();
        return Ok(new GuestQrSummaryDto(year.Value, grades));
    }

    /// <summary>QR per-school counts for one grade, ranked by ดีขึ้นไป (%) of คุณลักษณะฯ.</summary>
    [HttpGet("qr/schools/{grade}")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestQrSchoolsDto>> GetQrSchools(string grade, CancellationToken ct)
    {
        var g = grade.ToUpperInvariant();
        if (!QrGrades.Contains(g))
            return NotFound(new { message = $"unknown grade '{grade}' — expected P1..P6|M1..M6" });

        var year = await LatestYearOfAsync("qr", ct);
        if (year is null) return Ok(new GuestQrSchoolsDto(0, g, []));

        var rows = await _context.Database.SqlQuery<QrSchoolRow>($"""
            SELECT r."SmisCode" AS "SmisCode", s."NameTh" AS "SchoolName",
                   r."TotalStudents" AS "TotalStudents",
                   r."DcPass" AS "DcPass", r."DcGood" AS "DcGood", r."DcExcellent" AS "DcExcellent",
                   r."RcPass" AS "RcPass", r."RcGood" AS "RcGood", r."RcExcellent" AS "RcExcellent"
            FROM "QrSchoolResults" r
            JOIN "Schools" s ON s."SmisCode" = r."SmisCode" AND s."DeletedAt" IS NULL
            WHERE r."EducationYear" = {year} AND r."GradeLevel" = {g}
            """).ToListAsync(ct);

        return Ok(new GuestQrSchoolsDto(year.Value, g, rows
            .OrderByDescending(r => r.TotalStudents > 0
                ? (decimal)((r.DcGood ?? 0) + (r.DcExcellent ?? 0)) / r.TotalStudents!.Value : -1)
            .ToList()));
    }

    // ═══════════════════════ Plan #102 — Overview + School ══════════════════

    /// <summary>
    /// Academic overview: per-year headline series for every test type plus a
    /// per-school comparison table (latest year) powering the search grid.
    /// </summary>
    [HttpGet("overview")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestAcademicOverviewDto>> GetOverview(CancellationToken ct)
    {
        var onetYears = await _context.Database.SqlQuery<OnetYearGradeRow>($"""
            SELECT "EducationYear" AS "Year", "GradeLevel" AS "Grade",
                   COUNT(DISTINCT "SmisCode")::int AS "SchoolCount",
                   MAX("TestTakers")::int AS "TestTakers",
                   ROUND(SUM("MeanScore" * "TestTakers") / NULLIF(SUM("TestTakers"), 0), 2) AS "Mean"
            FROM "OnetSchoolResults" GROUP BY 1, 2 ORDER BY 1 DESC
            """).ToListAsync(ct);

        // Plan #111 D1 — weighted by ป.3 pupil counts, matching nt/summary.
        // COALESCE keeps a year without imported counts on the plain mean
        // rather than returning NULL.
        var ntYears = await _context.Database.SqlQuery<SimpleYearAggRow>($"""
            SELECT n."EducationYear" AS "Year", COUNT(*)::int AS "SchoolCount",
                   COALESCE(ROUND(SUM(n."MathScore"  * q."TotalStudents") / NULLIF(SUM(q."TotalStudents"), 0), 2), ROUND(AVG(n."MathScore"), 2))  AS "V1",
                   COALESCE(ROUND(SUM(n."ThaiScore"  * q."TotalStudents") / NULLIF(SUM(q."TotalStudents"), 0), 2), ROUND(AVG(n."ThaiScore"), 2))  AS "V2",
                   COALESCE(ROUND(SUM(n."TotalScore" * q."TotalStudents") / NULLIF(SUM(q."TotalStudents"), 0), 2), ROUND(AVG(n."TotalScore"), 2)) AS "V3"
            FROM "NtSchoolResults" n
            LEFT JOIN "QrSchoolResults" q
                   ON q."SmisCode" = n."SmisCode"
                  AND q."EducationYear" = n."EducationYear"
                  AND q."GradeLevel" = 'P3'
            GROUP BY 1 ORDER BY 1 DESC
            """).ToListAsync(ct);

        // Plan #111 D1 — weighted by ป.1 pupil counts, matching rt/summary.
        var rtYears = await _context.Database.SqlQuery<SimpleYearAggRow>($"""
            SELECT r."EducationYear" AS "Year", COUNT(*)::int AS "SchoolCount",
                   COALESCE(ROUND(SUM(r."ReadAloudPct" * q."TotalStudents") / NULLIF(SUM(q."TotalStudents"), 0), 2), ROUND(AVG(r."ReadAloudPct"), 2)) AS "V1",
                   COALESCE(ROUND(SUM(r."ReadCompPct"  * q."TotalStudents") / NULLIF(SUM(q."TotalStudents"), 0), 2), ROUND(AVG(r."ReadCompPct"), 2))  AS "V2",
                   COALESCE(ROUND(SUM(r."TotalPct"     * q."TotalStudents") / NULLIF(SUM(q."TotalStudents"), 0), 2), ROUND(AVG(r."TotalPct"), 2))     AS "V3"
            FROM "RtSchoolResults" r
            LEFT JOIN "QrSchoolResults" q
                   ON q."SmisCode" = r."SmisCode"
                  AND q."EducationYear" = r."EducationYear"
                  AND q."GradeLevel" = 'P1'
            GROUP BY 1 ORDER BY 1 DESC
            """).ToListAsync(ct);

        var qrYears = await _context.Database.SqlQuery<QrYearAggRow>($"""
            SELECT "EducationYear" AS "Year",
                   COUNT(DISTINCT "SmisCode")::int AS "SchoolCount",
                   COALESCE(SUM("TotalStudents"),0)::int AS "Students",
                   COALESCE(SUM("DcGood") + SUM("DcExcellent"),0)::int AS "DcGoodUp",
                   COALESCE(SUM("RcGood") + SUM("RcExcellent"),0)::int AS "RcGoodUp"
            FROM "QrSchoolResults" GROUP BY 1 ORDER BY 1 DESC
            """).ToListAsync(ct);

        var schoolRows = await _context.Database.SqlQuery<OverviewSchoolRow>($"""
            SELECT s."SmisCode" AS "SmisCode", s."NameTh" AS "SchoolName",
                   o."OnetMean" AS "OnetMean", n."TotalScore" AS "NtTotal", r."TotalPct" AS "RtTotal",
                   q."DcGoodUpPct" AS "QrGoodUpPct"
            FROM "Schools" s
            LEFT JOIN (SELECT "SmisCode", ROUND(AVG("MeanScore"), 2) AS "OnetMean"
                       FROM "OnetSchoolResults" WHERE "EducationYear" = (SELECT MAX("EducationYear") FROM "OnetSchoolResults")
                       GROUP BY 1) o ON o."SmisCode" = s."SmisCode"
            LEFT JOIN (SELECT "SmisCode", "TotalScore" FROM "NtSchoolResults"
                       WHERE "EducationYear" = (SELECT MAX("EducationYear") FROM "NtSchoolResults")) n ON n."SmisCode" = s."SmisCode"
            LEFT JOIN (SELECT "SmisCode", "TotalPct" FROM "RtSchoolResults"
                       WHERE "EducationYear" = (SELECT MAX("EducationYear") FROM "RtSchoolResults")) r ON r."SmisCode" = s."SmisCode"
            LEFT JOIN (SELECT "SmisCode",
                              ROUND(100.0 * (SUM("DcGood") + SUM("DcExcellent")) / NULLIF(SUM("TotalStudents"), 0), 2) AS "DcGoodUpPct"
                       FROM "QrSchoolResults" WHERE "EducationYear" = (SELECT MAX("EducationYear") FROM "QrSchoolResults")
                       GROUP BY 1) q ON q."SmisCode" = s."SmisCode"
            WHERE s."DeletedAt" IS NULL AND s."IsActive"
            ORDER BY s."NameTh"
            """).ToListAsync(ct);

        return Ok(new GuestAcademicOverviewDto(
            onetYears.GroupBy(r => r.Year)
                .OrderByDescending(g => g.Key)
                .Select(g => new GuestOnetYearDto(g.Key, g.Select(r =>
                    new GuestOnetYearGradeDto(r.Grade, r.SchoolCount, r.TestTakers, r.Mean)).ToList()))
                .ToList(),
            ntYears.Select(r => new GuestSimpleYearDto(r.Year, r.SchoolCount, r.V1, r.V2, r.V3)).ToList(),
            rtYears.Select(r => new GuestSimpleYearDto(r.Year, r.SchoolCount, r.V1, r.V2, r.V3)).ToList(),
            qrYears.Select(r => new GuestQrYearDto(r.Year, r.SchoolCount, r.Students,
                r.Students > 0 ? Math.Round(100m * r.DcGoodUp / r.Students, 2) : null,
                r.Students > 0 ? Math.Round(100m * r.RcGoodUp / r.Students, 2) : null)).ToList(),
            schoolRows));
    }

    /// <summary>Everything we know about one school across all tests and years.</summary>
    [HttpGet("school/{smisCode}")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestSchoolAcademicDto>> GetSchoolDetail(string smisCode, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(smisCode, @"^\d{8}$"))
            return NotFound(new { message = "invalid smisCode" });

        var school = await _context.Database.SqlQuery<SchoolHeadRow>($"""
            SELECT "SmisCode" AS "SmisCode", "NameTh" AS "SchoolName"
            FROM "Schools" WHERE "SmisCode" = {smisCode} AND "DeletedAt" IS NULL
            """).FirstOrDefaultAsync(ct);
        if (school is null) return NotFound(new { message = "school not found" });

        var onet = await _context.Database.SqlQuery<SchoolOnetRow>($"""
            SELECT "EducationYear" AS "Year", "GradeLevel" AS "Grade", "Subject" AS "Subject",
                   "TestTakers" AS "TestTakers", "MeanScore" AS "MeanScore"
            FROM "OnetSchoolResults" WHERE "SmisCode" = {smisCode} ORDER BY 1 DESC
            """).ToListAsync(ct);

        var nt = await _context.Database.SqlQuery<SchoolNtRow>($"""
            SELECT "EducationYear" AS "Year", "MathScore" AS "MathScore", "MathLevel" AS "MathLevel",
                   "ThaiScore" AS "ThaiScore", "ThaiLevel" AS "ThaiLevel",
                   "TotalScore" AS "TotalScore", "TotalLevel" AS "TotalLevel"
            FROM "NtSchoolResults" WHERE "SmisCode" = {smisCode} ORDER BY 1 DESC
            """).ToListAsync(ct);

        var rt = await _context.Database.SqlQuery<SchoolRtRow>($"""
            SELECT "EducationYear" AS "Year", "ReadAloudPct" AS "ReadAloudPct", "ReadAloudLevel" AS "ReadAloudLevel",
                   "ReadCompPct" AS "ReadCompPct", "ReadCompLevel" AS "ReadCompLevel",
                   "TotalPct" AS "TotalPct", "TotalLevel" AS "TotalLevel"
            FROM "RtSchoolResults" WHERE "SmisCode" = {smisCode} ORDER BY 1 DESC
            """).ToListAsync(ct);

        var qr = await _context.Database.SqlQuery<SchoolQrRow>($"""
            SELECT "EducationYear" AS "Year", "GradeLevel" AS "Grade", "TotalStudents" AS "TotalStudents",
                   "DcPass" AS "DcPass", "DcGood" AS "DcGood", "DcExcellent" AS "DcExcellent",
                   "RcPass" AS "RcPass", "RcGood" AS "RcGood", "RcExcellent" AS "RcExcellent"
            FROM "QrSchoolResults" WHERE "SmisCode" = {smisCode} ORDER BY 1 DESC, 2
            """).ToListAsync(ct);

        return Ok(new GuestSchoolAcademicDto(school.SmisCode, school.SchoolName, onet, nt, rt,
            qr.OrderByDescending(r => r.Year).ThenBy(r => Array.IndexOf(QrGrades, r.Grade)).ToList()));
    }

    /// <summary>
    /// Plan #111 D1 — area mean weighted by the number of pupils each school
    /// tested, i.e. Σ(score × pupils) / Σ(pupils). A plain mean of per-school
    /// averages gave a 20-pupil school the same weight as a 167-pupil one, so
    /// the published RT/NT figures were wrong (a user reported RT อ่านออกเสียง
    /// should read ~82.6, not 83.61). O-NET already computed it this way.
    /// Falls back to the unweighted mean when no counts are available, so the
    /// endpoint still answers for a year whose QR counts were never imported.
    /// </summary>
    private static decimal? WeightedAvg(IEnumerable<(decimal? Value, int? Weight)> rows)
    {
        var xs = rows.Where(r => r.Value.HasValue).ToList();
        if (xs.Count == 0) return null;

        var totalWeight = xs.Sum(r => (long)(r.Weight ?? 0));
        if (totalWeight <= 0)
            return Math.Round(xs.Average(r => r.Value!.Value), 2);

        var weighted = xs.Sum(r => r.Value!.Value * (r.Weight ?? 0));
        return Math.Round(weighted / totalWeight, 2);
    }

    private static decimal? Avg(IEnumerable<decimal?> values)
    {
        var xs = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return xs.Count == 0 ? null : Math.Round(xs.Average(), 2);
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

// ── Plan #102 — NT/RT/QR/overview/school row shapes + DTOs ───────────────────

public class NtRow
{
    public string SmisCode { get; set; } = "";
    /// <summary>ป.3 test-taker count, used to weight the area average (Plan #111 D1).</summary>
    public int? StudentCount { get; set; }
    public string SchoolName { get; set; } = "";
    public string? SchoolSize { get; set; }
    public decimal? MathScore { get; set; }
    public string? MathLevel { get; set; }
    public decimal? ThaiScore { get; set; }
    public string? ThaiLevel { get; set; }
    public decimal? TotalScore { get; set; }
    public string? TotalLevel { get; set; }
}

public class RtRow
{
    public string SmisCode { get; set; } = "";
    /// <summary>ป.1 test-taker count, used to weight the area average (Plan #111 D1).</summary>
    public int? StudentCount { get; set; }
    public string SchoolName { get; set; } = "";
    public string? SchoolSize { get; set; }
    public decimal? ReadAloudScore { get; set; }
    public decimal? ReadAloudPct { get; set; }
    public string? ReadAloudLevel { get; set; }
    public decimal? ReadCompScore { get; set; }
    public decimal? ReadCompPct { get; set; }
    public string? ReadCompLevel { get; set; }
    public decimal? TotalPct { get; set; }
    public string? TotalLevel { get; set; }
}

internal sealed class QrAggRow
{
    public string Grade { get; set; } = "";
    public int SchoolCount { get; set; }
    public int Students { get; set; }
    public int DcPass { get; set; }
    public int DcGood { get; set; }
    public int DcExcellent { get; set; }
    public int RcPass { get; set; }
    public int RcGood { get; set; }
    public int RcExcellent { get; set; }
}

public class QrSchoolRow
{
    public string SmisCode { get; set; } = "";
    public string SchoolName { get; set; } = "";
    public int? TotalStudents { get; set; }
    public int? DcPass { get; set; }
    public int? DcGood { get; set; }
    public int? DcExcellent { get; set; }
    public int? RcPass { get; set; }
    public int? RcGood { get; set; }
    public int? RcExcellent { get; set; }
}

internal sealed class OnetYearGradeRow
{
    public int Year { get; set; }
    public string Grade { get; set; } = "";
    public int SchoolCount { get; set; }
    public int TestTakers { get; set; }
    public decimal? Mean { get; set; }
}

internal sealed class SimpleYearAggRow
{
    public int Year { get; set; }
    public int SchoolCount { get; set; }
    public decimal? V1 { get; set; }
    public decimal? V2 { get; set; }
    public decimal? V3 { get; set; }
}

internal sealed class QrYearAggRow
{
    public int Year { get; set; }
    public int SchoolCount { get; set; }
    public int Students { get; set; }
    public int DcGoodUp { get; set; }
    public int RcGoodUp { get; set; }
}

public class OverviewSchoolRow
{
    public string SmisCode { get; set; } = "";
    public string SchoolName { get; set; } = "";
    public decimal? OnetMean { get; set; }
    public decimal? NtTotal { get; set; }
    public decimal? RtTotal { get; set; }
    public decimal? QrGoodUpPct { get; set; }
}

internal sealed class SchoolHeadRow
{
    public string SmisCode { get; set; } = "";
    public string SchoolName { get; set; } = "";
}

public class SchoolOnetRow
{
    public int Year { get; set; }
    public string Grade { get; set; } = "";
    public string Subject { get; set; } = "";
    public int TestTakers { get; set; }
    public decimal MeanScore { get; set; }
}

public class SchoolNtRow
{
    public int Year { get; set; }
    public decimal? MathScore { get; set; }
    public string? MathLevel { get; set; }
    public decimal? ThaiScore { get; set; }
    public string? ThaiLevel { get; set; }
    public decimal? TotalScore { get; set; }
    public string? TotalLevel { get; set; }
}

public class SchoolRtRow
{
    public int Year { get; set; }
    public decimal? ReadAloudPct { get; set; }
    public string? ReadAloudLevel { get; set; }
    public decimal? ReadCompPct { get; set; }
    public string? ReadCompLevel { get; set; }
    public decimal? TotalPct { get; set; }
    public string? TotalLevel { get; set; }
}

public class SchoolQrRow
{
    public int Year { get; set; }
    public string Grade { get; set; } = "";
    public int? TotalStudents { get; set; }
    public int? DcPass { get; set; }
    public int? DcGood { get; set; }
    public int? DcExcellent { get; set; }
    public int? RcPass { get; set; }
    public int? RcGood { get; set; }
    public int? RcExcellent { get; set; }
}

public record GuestLevelCountDto(string Level, int Count);

public record GuestNtSummaryDto(
    int Year, int SchoolCount,
    decimal? AvgMath, decimal? AvgThai, decimal? AvgTotal,
    List<GuestLevelCountDto> MathLevels, List<GuestLevelCountDto> ThaiLevels, List<GuestLevelCountDto> TotalLevels);

public record GuestNtSchoolsDto(int Year, List<NtRow> Schools);

public record GuestRtSummaryDto(
    int Year, int SchoolCount,
    decimal? AvgReadAloud, decimal? AvgReadComp, decimal? AvgTotal,
    List<GuestLevelCountDto> ReadAloudLevels, List<GuestLevelCountDto> ReadCompLevels, List<GuestLevelCountDto> TotalLevels);

public record GuestRtSchoolsDto(int Year, List<RtRow> Schools);

public record GuestQrGradeDto(
    string Grade, int SchoolCount, int Students,
    int DcPass, int DcGood, int DcExcellent,
    int RcPass, int RcGood, int RcExcellent);

public record GuestQrSummaryDto(int Year, List<GuestQrGradeDto> Grades);

public record GuestQrSchoolsDto(int Year, string Grade, List<QrSchoolRow> Schools);

public record GuestOnetYearGradeDto(string Grade, int SchoolCount, int TestTakers, decimal? Mean);

public record GuestOnetYearDto(int Year, List<GuestOnetYearGradeDto> Grades);

public record GuestSimpleYearDto(int Year, int SchoolCount, decimal? V1, decimal? V2, decimal? V3);

public record GuestQrYearDto(int Year, int SchoolCount, int Students, decimal? DcGoodUpPct, decimal? RcGoodUpPct);

public record GuestAcademicOverviewDto(
    List<GuestOnetYearDto> Onet,
    List<GuestSimpleYearDto> Nt,
    List<GuestSimpleYearDto> Rt,
    List<GuestQrYearDto> Qr,
    List<OverviewSchoolRow> Schools);

public record GuestSchoolAcademicDto(
    string SmisCode, string SchoolName,
    List<SchoolOnetRow> Onet, List<SchoolNtRow> Nt, List<SchoolRtRow> Rt, List<SchoolQrRow> Qr);
