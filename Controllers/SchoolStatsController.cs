using Gateway.Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    public SchoolStatsController(GatewayDbContext db) { _db = db; }

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
