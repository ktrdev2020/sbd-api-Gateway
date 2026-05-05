using Gateway.Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #26 — Grade-level student count and personnel-type count per academic year.
/// Source: aplan PDF บทที่ 1 (สภาพปัจจุบัน).
/// </summary>
[ApiController]
[Route("api/v1/school/{schoolCode}")]
[Authorize]
public class SchoolStatsController : ControllerBase
{
    private readonly GatewayDbContext _db;

    // GatewayDbContext is registered as SbdDbContext in DI — cast on constructor.
    public SchoolStatsController(SbdDbContext db) { _db = (GatewayDbContext)db; }

    // ─── Grade Stats (จำนวนนักเรียนแยกระดับชั้น) ─────────────────────────

    [HttpGet("grade-stats")]
    public async Task<ActionResult<List<GradeStatDto>>> GetGradeStats(string schoolCode, [FromQuery] int? year)
    {
        var academicYear = year ?? CurrentAcademicYear();
        var rows = await _db.SchoolGradeStats
            .Where(s => s.SchoolCode == schoolCode && s.AcademicYear == academicYear)
            .OrderBy(s => s.GradeOrder)
            .AsNoTracking()
            .ToListAsync();
        return Ok(rows.Select(MapGrade).ToList());
    }

    [HttpPut("grade-stats")]
    public async Task<ActionResult<List<GradeStatDto>>> SaveGradeStats(
        string schoolCode, [FromQuery] int? year, [FromBody] List<GradeStatUpsertRow> rows)
    {
        var academicYear = year ?? CurrentAcademicYear();

        // Bulk replace (D6) — delete existing for that year, then insert new
        var existing = _db.SchoolGradeStats
            .Where(s => s.SchoolCode == schoolCode && s.AcademicYear == academicYear);
        _db.SchoolGradeStats.RemoveRange(existing);
        await _db.SaveChangesAsync();

        var newRows = rows.Select((r, idx) => new SchoolGradeStat
        {
            SchoolCode = schoolCode,
            AcademicYear = academicYear,
            Grade = r.Grade,
            GradeOrder = r.GradeOrder ?? idx,
            MaleCount = r.MaleCount,
            FemaleCount = r.FemaleCount,
            ClassroomCount = r.ClassroomCount,
            UpdatedAt = DateTimeOffset.UtcNow,
        }).ToList();

        await _db.SchoolGradeStats.AddRangeAsync(newRows);
        await _db.SaveChangesAsync();

        // Update Schools.StudentCount cache (denormalized — D4)
        var totalStudents = newRows.Sum(r => r.MaleCount + r.FemaleCount);
        var school = await _db.Schools.FirstOrDefaultAsync(s => s.SchoolCode == schoolCode);
        if (school != null) { school.StudentCount = totalStudents; await _db.SaveChangesAsync(); }

        return Ok(newRows.OrderBy(r => r.GradeOrder).Select(MapGrade).ToList());
    }

    // ─── Personnel Type Stats (จำนวนบุคลากรแยกประเภท) ─────────────────────

    [HttpGet("personnel-type-stats")]
    public async Task<ActionResult<List<PersonnelTypeStatDto>>> GetPersonnelTypeStats(
        string schoolCode, [FromQuery] int? year)
    {
        var academicYear = year ?? CurrentAcademicYear();
        var rows = await _db.SchoolPersonnelTypeStats
            .Where(s => s.SchoolCode == schoolCode && s.AcademicYear == academicYear)
            .OrderBy(s => s.TypeOrder)
            .AsNoTracking()
            .ToListAsync();
        return Ok(rows.Select(MapType).ToList());
    }

    [HttpPut("personnel-type-stats")]
    public async Task<ActionResult<List<PersonnelTypeStatDto>>> SavePersonnelTypeStats(
        string schoolCode, [FromQuery] int? year, [FromBody] List<PersonnelTypeStatUpsertRow> rows)
    {
        var academicYear = year ?? CurrentAcademicYear();

        var existing = _db.SchoolPersonnelTypeStats
            .Where(s => s.SchoolCode == schoolCode && s.AcademicYear == academicYear);
        _db.SchoolPersonnelTypeStats.RemoveRange(existing);
        await _db.SaveChangesAsync();

        var newRows = rows.Select((r, idx) => new SchoolPersonnelTypeStat
        {
            SchoolCode = schoolCode,
            AcademicYear = academicYear,
            PersonnelType = r.PersonnelType,
            TypeOrder = r.TypeOrder ?? idx,
            MaleCount = r.MaleCount,
            FemaleCount = r.FemaleCount,
            UpdatedAt = DateTimeOffset.UtcNow,
        }).ToList();

        await _db.SchoolPersonnelTypeStats.AddRangeAsync(newRows);
        await _db.SaveChangesAsync();

        // Update Schools.TeacherCount cache (denormalized — D4)
        var totalPersonnel = newRows.Sum(r => r.MaleCount + r.FemaleCount);
        var school = await _db.Schools.FirstOrDefaultAsync(s => s.SchoolCode == schoolCode);
        if (school != null) { school.TeacherCount = totalPersonnel; await _db.SaveChangesAsync(); }

        return Ok(newRows.OrderBy(r => r.TypeOrder).Select(MapType).ToList());
    }

    // ─── Plan #26 — Auto-fetch from live source data (Student/Personnel) ──────

    /// <summary>
    /// Aggregates Personnel × PersonnelType × Gender for active assignments
    /// at the given school, returning rows ready to populate the editor.
    /// Personnel data lives in same Gateway DB so query is direct (no proxy).
    /// </summary>
    [HttpGet("personnel-type-stats/auto")]
    public async Task<ActionResult<List<PersonnelTypeStatDto>>> AutoFetchPersonnel(string schoolCode)
    {
        // Pull raw assignments (PersonnelType code + Position string) so we can
        // do Position-aware classification — Director code spans both ผอ. and รองผอ.;
        // Staff code spans ธุรการ / พี่เลี้ยง / นักการภารโรง.
        var raw = await (
            from psa in _db.Set<SBD.Domain.Entities.PersonnelSchoolAssignment>().AsNoTracking()
            join p in _db.Personnel.AsNoTracking() on psa.PersonnelId equals p.Id
            join pt in _db.Set<SBD.Domain.Entities.PersonnelType>().AsNoTracking() on p.PersonnelTypeId equals pt.Id
            where psa.SchoolCode == schoolCode
              && (psa.EndDate == null || psa.EndDate >= DateOnly.FromDateTime(DateTime.Today))
            select new
            {
                code = pt.Code,
                position = psa.Position ?? string.Empty,
                gender = p.Gender,
            }
        ).ToListAsync();

        // Bucket each row → display label + sort order. Matches DEFAULT_PERSONNEL_TYPE_ROWS.
        static (string name, int order) Classify(string code, string position)
        {
            switch (code)
            {
                case "Director":
                    return position.Contains("รอง")
                        ? ("รองผู้อำนวยการ", 1) : ("ผู้อำนวยการ", 0);
                case "Teacher":       return ("ครู", 2);
                case "GovEmployee":   return ("พนักงานราชการ", 3);
                case "PermanentStaff":return ("ลูกจ้างประจำ", 4);
                case "TempStaff":     return ("ลูกจ้างชั่วคราว", 5);
                case "Staff":
                    if (position.Contains("ธุรการ")) return ("ธุรการ", 6);
                    if (position.Contains("ภารโรง") || position.Contains("นักการ")) return ("นักการภารโรง", 7);
                    if (position.Contains("พี่เลี้ยง")) return ("พี่เลี้ยง", 8);
                    return ("บุคลากรสนับสนุน", 9);
                default: return (code, 99);
            }
        }

        var grouped = raw
            .Select(r => new { bucket = Classify(r.code, r.position), r.gender })
            .GroupBy(x => x.bucket)
            .Select(g => new PersonnelTypeStatDto
            {
                Id = 0,
                PersonnelType = g.Key.name,
                TypeOrder = g.Key.order,
                MaleCount = g.Count(x => x.gender == 'M' || x.gender == 'm'),
                FemaleCount = g.Count(x => x.gender == 'F' || x.gender == 'f'),
            })
            .OrderBy(x => x.TypeOrder)
            .ToList();

        return Ok(grouped);
    }

    private static int CurrentAcademicYear()
    {
        var now = DateTime.Now;
        var year = now.Year + 543;
        if (now.Month < 5) year -= 1;
        return year;
    }

    private static GradeStatDto MapGrade(SchoolGradeStat s) => new()
    {
        Id = s.Id,
        Grade = s.Grade,
        GradeOrder = s.GradeOrder,
        MaleCount = s.MaleCount,
        FemaleCount = s.FemaleCount,
        ClassroomCount = s.ClassroomCount,
    };

    private static PersonnelTypeStatDto MapType(SchoolPersonnelTypeStat s) => new()
    {
        Id = s.Id,
        PersonnelType = s.PersonnelType,
        TypeOrder = s.TypeOrder,
        MaleCount = s.MaleCount,
        FemaleCount = s.FemaleCount,
    };
}

public class GradeStatDto
{
    public long Id { get; set; }
    public string Grade { get; set; } = string.Empty;
    public int GradeOrder { get; set; }
    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }
    public int? ClassroomCount { get; set; }
}

public class GradeStatUpsertRow
{
    public string Grade { get; set; } = string.Empty;
    public int? GradeOrder { get; set; }
    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }
    public int? ClassroomCount { get; set; }
}

public class PersonnelTypeStatDto
{
    public long Id { get; set; }
    public string PersonnelType { get; set; } = string.Empty;
    public int TypeOrder { get; set; }
    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }
}

public class PersonnelTypeStatUpsertRow
{
    public string PersonnelType { get; set; } = string.Empty;
    public int? TypeOrder { get; set; }
    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }
}
