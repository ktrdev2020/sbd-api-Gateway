using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway;

public static class AcademicCalendarSeedData
{
    public static async Task SeedAsync(SbdDbContext db)
    {
        // ── FiscalYear (ปีงบประมาณ: ต.ค. - ก.ย.) ──
        if (!await db.FiscalYears.AnyAsync())
        {
            var fiscalYears = new[]
            {
                new FiscalYear { Year = 2567, NameTh = "ปีงบประมาณ 2567", StartDate = new DateOnly(2023, 10, 1), EndDate = new DateOnly(2024, 9, 30) },
                new FiscalYear { Year = 2568, NameTh = "ปีงบประมาณ 2568", StartDate = new DateOnly(2024, 10, 1), EndDate = new DateOnly(2025, 9, 30) },
                new FiscalYear { Year = 2569, NameTh = "ปีงบประมาณ 2569", StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 9, 30) },
                new FiscalYear { Year = 2570, NameTh = "ปีงบประมาณ 2570", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 9, 30) },
            };
            db.FiscalYears.AddRange(fiscalYears);
            await db.SaveChangesAsync();
            Console.WriteLine("[Seed] FiscalYears 2567-2570 created.");

            // ── AcademicYear (ปีการศึกษา: พ.ค. - มี.ค.) — คาบเกี่ยว 2 ปีงบฯ ──
            var fy = fiscalYears.ToDictionary(f => f.Year);
            var academicYears = new[]
            {
                new AcademicYear { Year = 2567, NameTh = "ปีการศึกษา 2567", FiscalYearStartId = fy[2567].Id, FiscalYearEndId = fy[2568].Id, StartDate = new DateOnly(2024, 5, 16), EndDate = new DateOnly(2025, 3, 31) },
                new AcademicYear { Year = 2568, NameTh = "ปีการศึกษา 2568", FiscalYearStartId = fy[2568].Id, FiscalYearEndId = fy[2569].Id, StartDate = new DateOnly(2025, 5, 16), EndDate = new DateOnly(2026, 3, 31) },
                new AcademicYear { Year = 2569, NameTh = "ปีการศึกษา 2569", FiscalYearStartId = fy[2569].Id, FiscalYearEndId = fy[2570].Id, StartDate = new DateOnly(2026, 5, 16), EndDate = new DateOnly(2027, 3, 31) },
                new AcademicYear { Year = 2570, NameTh = "ปีการศึกษา 2570", FiscalYearStartId = fy[2570].Id, FiscalYearEndId = null, StartDate = new DateOnly(2027, 5, 16), EndDate = new DateOnly(2028, 3, 31) },
            };
            db.AcademicYears.AddRange(academicYears);
            await db.SaveChangesAsync();
            Console.WriteLine("[Seed] AcademicYears 2567-2570 created.");

            // ── Term (ภาคเรียน) ──
            var terms = new[]
            {
                new Term { TermNumber = 1, NameTh = "ภาคเรียนที่ 1", NameEn = "First Semester" },
                new Term { TermNumber = 2, NameTh = "ภาคเรียนที่ 2", NameEn = "Second Semester" },
                new Term { TermNumber = 3, NameTh = "ภาคเรียนฤดูร้อน", NameEn = "Summer Term" },
            };
            db.Terms.AddRange(terms);
            await db.SaveChangesAsync();
            Console.WriteLine("[Seed] Terms (3) created.");

            // ── AcademicYearTerm mapping ──
            var ayTerms = new List<AcademicYearTerm>();
            foreach (var ay in academicYears)
            {
                var startYear = ay.Year + 543 > 2600 ? ay.Year - 543 : ay.Year; // Convert พ.ศ. → approximate
                // Approximate dates based on Thai academic calendar
                var ceStartYear = ay.StartDate.Year;
                ayTerms.Add(new AcademicYearTerm { AcademicYearId = ay.Id, TermId = terms[0].Id, StartDate = new DateOnly(ceStartYear, 5, 16), EndDate = new DateOnly(ceStartYear, 10, 10) });
                ayTerms.Add(new AcademicYearTerm { AcademicYearId = ay.Id, TermId = terms[1].Id, StartDate = new DateOnly(ceStartYear, 11, 1), EndDate = new DateOnly(ceStartYear + 1, 3, 31) });
                ayTerms.Add(new AcademicYearTerm { AcademicYearId = ay.Id, TermId = terms[2].Id, StartDate = new DateOnly(ceStartYear + 1, 4, 1), EndDate = new DateOnly(ceStartYear + 1, 5, 15) });
            }
            db.AcademicYearTerms.AddRange(ayTerms);
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] AcademicYearTerms ({ayTerms.Count}) created.");
        }

        // ── GradeGroup + Grade ──
        if (!await db.GradeGroups.AnyAsync())
        {
            var groups = new (string code, string name, int sort, (string code, string name)[] grades)[]
            {
                ("pre-school", "ปฐมวัย", 0, new[] { ("อ.1", "อนุบาล 1"), ("อ.2", "อนุบาล 2"), ("อ.3", "อนุบาล 3") }),
                ("level-1", "ช่วงชั้นที่ 1 (ป.1-3)", 1, new[] { ("ป.1", "ประถมศึกษาปีที่ 1"), ("ป.2", "ประถมศึกษาปีที่ 2"), ("ป.3", "ประถมศึกษาปีที่ 3") }),
                ("level-2", "ช่วงชั้นที่ 2 (ป.4-6)", 2, new[] { ("ป.4", "ประถมศึกษาปีที่ 4"), ("ป.5", "ประถมศึกษาปีที่ 5"), ("ป.6", "ประถมศึกษาปีที่ 6") }),
                ("level-3", "ช่วงชั้นที่ 3 (ม.1-3)", 3, new[] { ("ม.1", "มัธยมศึกษาปีที่ 1"), ("ม.2", "มัธยมศึกษาปีที่ 2"), ("ม.3", "มัธยมศึกษาปีที่ 3") }),
                ("level-4", "ช่วงชั้นที่ 4 (ม.4-6)", 4, new[] { ("ม.4", "มัธยมศึกษาปีที่ 4"), ("ม.5", "มัธยมศึกษาปีที่ 5"), ("ม.6", "มัธยมศึกษาปีที่ 6") }),
            };

            var sortIndex = 0;
            foreach (var (code, name, sort, grades) in groups)
            {
                var group = new GradeGroup { Code = code, NameTh = name, SortOrder = sort };
                db.GradeGroups.Add(group);
                await db.SaveChangesAsync();

                foreach (var (gCode, gName) in grades)
                {
                    db.Grades.Add(new Grade { Code = gCode, NameTh = gName, GradeGroupId = group.Id, SortOrder = ++sortIndex });
                }
            }
            await db.SaveChangesAsync();
            Console.WriteLine("[Seed] GradeGroups (5) + Grades (15) created.");
        }
    }
}
