using SBD.Infrastructure.Data;
using SBD.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gateway;

/// <summary>
/// Seeds demo personnel for school 159 (and a few other schools) so that
/// AI tools like get_school_personnel return real data during development / QA.
/// All inserts are idempotent — checked by PersonnelCode before inserting.
/// </summary>
public static class PersonnelSeedData
{
    public static async Task SeedAsync(SbdDbContext db)
    {
        // ── 1. Seed Personnel rows ──────────────────────────────────────────
        var people = new[]
        {
            new { Code="T001", First="สมชาย",   Last="ใจดี",     Type="Teacher",  Gender='M', Subject="คณิตศาสตร์",  School=159 },
            new { Code="T002", First="สมหญิง",  Last="รักเรียน", Type="Teacher",  Gender='F', Subject="ภาษาไทย",     School=159 },
            new { Code="T003", First="วิชัย",   Last="มานะดี",   Type="Teacher",  Gender='M', Subject="วิทยาศาสตร์", School=159 },
            new { Code="T004", First="นิตยา",   Last="สุขใส",    Type="Teacher",  Gender='F', Subject="สังคมศึกษา",  School=159 },
            new { Code="T005", First="ประเสริฐ",Last="ทองคำ",    Type="Teacher",  Gender='M', Subject="ภาษาอังกฤษ",  School=159 },
            new { Code="T006", First="รัตนา",   Last="แก้วใส",   Type="Teacher",  Gender='F', Subject="ศิลปะ",       School=159 },
            new { Code="T007", First="อนุชา",   Last="พงษ์ดี",   Type="Teacher",  Gender='M', Subject="พลศึกษา",     School=159 },
            new { Code="D001", First="สุรชัย",  Last="เจริญดี",  Type="Director", Gender='M', Subject=null,          School=159 },
            new { Code="S001", First="พิมพ์ใจ", Last="เชื้อดี",  Type="Staff",    Gender='F', Subject=null,          School=159 },
        };

        foreach (var p in people)
        {
            if (await db.Personnel.AnyAsync(x => x.PersonnelCode == p.Code)) continue;

            var personnel = new Personnel
            {
                PersonnelCode = p.Code,
                FirstName     = p.First,
                LastName      = p.Last,
                PersonnelType = p.Type,
                Gender        = p.Gender,
                SubjectArea   = p.Subject,
                UpdatedAt     = DateTimeOffset.UtcNow,
            };
            db.Personnel.Add(personnel);
            await db.SaveChangesAsync(); // flush to get Id

            // ── 2. Assign to school ──────────────────────────────────────
            var alreadyAssigned = await db.PersonnelSchoolAssignments
                .AnyAsync(a => a.PersonnelId == personnel.Id && a.SchoolId == p.School);

            if (!alreadyAssigned)
            {
                db.PersonnelSchoolAssignments.Add(new PersonnelSchoolAssignment
                {
                    PersonnelId = personnel.Id,
                    SchoolId    = p.School,
                    Position    = p.Type == "Director" ? "ผู้อำนวยการโรงเรียน"
                                : p.Type == "Staff"    ? "เจ้าหน้าที่ธุรการ"
                                :                        "ครู",
                    IsPrimary   = true,
                    StartDate   = new DateOnly(2024, 5, 1),
                });
                await db.SaveChangesAsync();
            }

            Console.WriteLine($"[Seed] Personnel '{p.Code} {p.First} {p.Last}' → School {p.School}");
        }
    }
}
