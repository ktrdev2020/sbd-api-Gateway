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
        var people = new (string Code, string First, string Last, string Type, char Gender, string? Subject, int School)[]
        {
            ("T001", "สมชาย",    "ใจดี",     "Teacher",  'M', "คณิตศาสตร์",  159),
            ("T002", "สมหญิง",   "รักเรียน", "Teacher",  'F', "ภาษาไทย",     159),
            ("T003", "วิชัย",    "มานะดี",   "Teacher",  'M', "วิทยาศาสตร์", 159),
            ("T004", "นิตยา",    "สุขใส",    "Teacher",  'F', "สังคมศึกษา",  159),
            ("T005", "ประเสริฐ", "ทองคำ",    "Teacher",  'M', "ภาษาอังกฤษ",  159),
            ("T006", "รัตนา",    "แก้วใส",   "Teacher",  'F', "ศิลปะ",       159),
            ("T007", "อนุชา",    "พงษ์ดี",   "Teacher",  'M', "พลศึกษา",     159),
            ("D001", "สุรชัย",   "เจริญดี",  "Director", 'M', null,           159),
            ("S001", "พิมพ์ใจ",  "เชื้อดี",  "Staff",    'F', null,           159),
        };

        foreach (var (Code, First, Last, Type, Gender, Subject, School) in people)
        {
            if (await db.Personnel.AnyAsync(x => x.PersonnelCode == Code)) continue;

            var personnel = new Personnel
            {
                PersonnelCode = Code,
                FirstName     = First,
                LastName      = Last,
                PersonnelType = Type,
                Gender        = Gender,
                SubjectArea   = Subject,
                UpdatedAt     = DateTimeOffset.UtcNow,
            };
            db.Personnel.Add(personnel);
            await db.SaveChangesAsync(); // flush to get Id

            // ── 2. Assign to school ──────────────────────────────────────
            var alreadyAssigned = await db.PersonnelSchoolAssignments
                .AnyAsync(a => a.PersonnelId == personnel.Id && a.SchoolId == School);

            if (!alreadyAssigned)
            {
                db.PersonnelSchoolAssignments.Add(new PersonnelSchoolAssignment
                {
                    PersonnelId = personnel.Id,
                    SchoolId    = School,
                    Position    = Type == "Director" ? "ผู้อำนวยการโรงเรียน"
                                : Type == "Staff"    ? "เจ้าหน้าที่ธุรการ"
                                :                      "ครู",
                    IsPrimary   = true,
                    StartDate   = new DateOnly(2024, 5, 1),
                });
                await db.SaveChangesAsync();
            }

            Console.WriteLine($"[Seed] Personnel '{Code} {First} {Last}' → School {School}");
        }
    }
}
