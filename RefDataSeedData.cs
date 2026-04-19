using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway;

/// <summary>
/// Seeds reference lookup tables: Specialties and SubjectAreas.
/// Idempotent — inserts only missing rows identified by Code.
/// </summary>
public static class RefDataSeedData
{
    public static async Task SeedAsync(SbdDbContext db)
    {
        await SeedPersonnelTypesAsync(db);
        await SeedEducationLevelsAsync(db);
        await SeedSubjectAreasAsync(db);
        await SeedSpecialtiesAsync(db);
    }

    // ── ประเภทบุคลากร ─────────────────────────────────────────────────────────────
    private static async Task SeedPersonnelTypesAsync(SbdDbContext db)
    {
        var seed = new (string Code, string NameTh, string NameEn, string PositionCategory, int SortOrder)[]
        {
            ("Director",       "ผู้บริหารสถานศึกษา",          "School Director",      "ผู้บริหาร",   1),
            ("Teacher",        "ครู (ข้าราชการ)",              "Government Teacher",   "ครู",         2),
            ("GovEmployee",    "พนักงานราชการ",                "Government Employee",  "เจ้าหน้าที่", 3),
            ("PermanentStaff", "ลูกจ้างประจำ",                "Permanent Staff",      "บุคลากรอื่น", 4),
            ("TempStaff",      "ลูกจ้างชั่วคราว / อัตราจ้าง","Temporary Staff",      "บุคลากรอื่น", 5),
            ("Staff",          "บุคลากรสนับสนุน",              "Support Staff",        "บุคลากรอื่น", 6),
        };

        var existing = await db.PersonnelTypes.Select(p => p.Code).ToHashSetAsync();
        foreach (var (code, nameTh, nameEn, positionCategory, sortOrder) in seed)
        {
            if (!existing.Contains(code))
                db.PersonnelTypes.Add(new SBD.Domain.Entities.PersonnelType
                {
                    Code             = code,
                    NameTh           = nameTh,
                    NameEn           = nameEn,
                    PositionCategory = positionCategory,
                    SortOrder        = sortOrder,
                    IsActive         = true,
                });
        }
        await db.SaveChangesAsync();
    }

    // ── ระดับวุฒิการศึกษา ────────────────────────────────────────────────────────
    // SchoolSeedData.SeedAsync() has an early-exit guard (returns when Schools exist),
    // so EducationLevels were never seeded for existing databases. Moved here.
    private static async Task SeedEducationLevelsAsync(SbdDbContext db)
    {
        var seed = new (string Code, string NameTh, string NameEn, int Level)[]
        {
            ("p6",    "ประถมศึกษา",                        "Primary",                     1),
            ("m3",    "มัธยมศึกษาตอนต้น",                  "Lower Secondary",             2),
            ("m6",    "มัธยมศึกษาตอนปลาย",                 "Upper Secondary",             3),
            ("pvc",   "ประกาศนียบัตรวิชาชีพ (ปวช.)",       "Vocational Certificate",      4),
            ("pvc2",  "ประกาศนียบัตรวิชาชีพชั้นสูง (ปวส.)", "High Vocational Certificate", 5),
            ("ba",    "ปริญญาตรี",                          "Bachelor's Degree",           6),
            ("ma",    "ปริญญาโท",                           "Master's Degree",             7),
            ("phd",   "ปริญญาเอก",                          "Doctoral Degree",             8),
            ("other", "อื่นๆ",                              "Other",                       0),
        };

        var existing = await db.EducationLevels.Select(e => e.Code).ToHashSetAsync();
        foreach (var (code, nameTh, nameEn, level) in seed)
        {
            if (!existing.Contains(code))
            {
                db.EducationLevels.Add(new EducationLevel
                {
                    Code = code, NameTh = nameTh, NameEn = nameEn,
                    Level = level, IsActive = true,
                });
            }
        }
        await db.SaveChangesAsync();
    }

    // ── กลุ่มสาระการเรียนรู้ 8 กลุ่ม + กิจกรรมพัฒนาผู้เรียน ─────────────────────
    private static async Task SeedSubjectAreasAsync(SbdDbContext db)
    {
        var seed = new (string Code, string NameTh, string? NameEn, int SortOrder)[]
        {
            ("thai",     "ภาษาไทย",                        "Thai Language",           1),
            ("math",     "คณิตศาสตร์",                     "Mathematics",             2),
            ("science",  "วิทยาศาสตร์และเทคโนโลยี",       "Science and Technology",  3),
            ("social",   "สังคมศึกษา ศาสนา และวัฒนธรรม",  "Social Studies",          4),
            ("art",      "ศิลปะ",                          "Arts",                    5),
            ("health",   "สุขศึกษาและพลศึกษา",             "Health and Physical Ed.", 6),
            ("career",   "การงานอาชีพ",                    "Occupational Education",  7),
            ("foreign",  "ภาษาต่างประเทศ",                 "Foreign Languages",       8),
            ("activity", "กิจกรรมพัฒนาผู้เรียน",           "Student Development",     9),
        };

        var existing = await db.SubjectAreas.Select(s => s.Code).ToHashSetAsync();
        foreach (var (code, nameTh, nameEn, sort) in seed)
        {
            if (!existing.Contains(code))
            {
                db.SubjectAreas.Add(new SubjectArea
                {
                    Code = code, NameTh = nameTh, NameEn = nameEn,
                    SortOrder = sort, IsActive = true,
                });
            }
        }
        await db.SaveChangesAsync();
    }

    // ── วิชาเอก / ความชำนาญ ──────────────────────────────────────────────────────
    private static async Task SeedSpecialtiesAsync(SbdDbContext db)
    {
        var seed = new (string Code, string NameTh, string Category, int SortOrder)[]
        {
            // ครู — กลุ่มสาระ 8 กลุ่ม
            ("t_thai",    "ภาษาไทย",                  "ครู", 10),
            ("t_math",    "คณิตศาสตร์",               "ครู", 20),
            ("t_sci",     "วิทยาศาสตร์",              "ครู", 30),
            ("t_bio",     "ชีววิทยา",                 "ครู", 31),
            ("t_chem",    "เคมี",                     "ครู", 32),
            ("t_phys",    "ฟิสิกส์",                  "ครู", 33),
            ("t_social",  "สังคมศึกษา",               "ครู", 40),
            ("t_hist",    "ประวัติศาสตร์",             "ครู", 41),
            ("t_art",     "ศิลปะ",                    "ครู", 50),
            ("t_music",   "ดนตรี",                    "ครู", 51),
            ("t_draw",    "ทัศนศิลป์",                "ครู", 52),
            ("t_dance",   "นาฏศิลป์",                 "ครู", 53),
            ("t_health",  "สุขศึกษา",                 "ครู", 60),
            ("t_pe",      "พลศึกษา",                  "ครู", 61),
            ("t_career",  "การงานอาชีพ",              "ครู", 70),
            ("t_tech",    "เทคโนโลยี",                "ครู", 71),
            ("t_comp",    "คอมพิวเตอร์",              "ครู", 72),
            ("t_eng",     "ภาษาอังกฤษ",               "ครู", 80),
            ("t_chinese", "ภาษาจีน",                  "ครู", 81),
            ("t_japanese","ภาษาญี่ปุ่น",              "ครู", 82),
            ("t_french",  "ภาษาฝรั่งเศส",             "ครู", 83),
            ("t_edu",     "การศึกษาปฐมวัย",            "ครู", 90),
            ("t_spec",    "การศึกษาพิเศษ",             "ครู", 91),
            ("t_guid",    "แนะแนว",                   "ครู", 92),
            ("t_lib",     "บรรณารักษ์",               "ครู", 93),

            // บุคลากรสนับสนุน
            ("s_fin",    "การเงินและบัญชี",            "บุคลากรสนับสนุน", 110),
            ("s_admin",  "การบริหารจัดการ",            "บุคลากรสนับสนุน", 120),
            ("s_hr",     "ทรัพยากรบุคคล",             "บุคลากรสนับสนุน", 130),
            ("s_it",     "เทคโนโลยีสารสนเทศ",         "บุคลากรสนับสนุน", 140),
            ("s_law",    "นิติกรรม",                   "บุคลากรสนับสนุน", 150),
            ("s_plan",   "นโยบายและแผน",               "บุคลากรสนับสนุน", 160),
            ("s_pr",     "ประชาสัมพันธ์",              "บุคลากรสนับสนุน", 170),
            ("s_supply", "พัสดุ",                      "บุคลากรสนับสนุน", 180),
            ("s_stat",   "สถิติ",                      "บุคลากรสนับสนุน", 190),
            ("s_audit",  "ตรวจสอบภายใน",              "บุคลากรสนับสนุน", 200),
        };

        var existing = await db.Specialties.Select(s => s.Code).ToHashSetAsync();
        foreach (var (code, nameTh, category, sort) in seed)
        {
            if (!existing.Contains(code))
            {
                db.Specialties.Add(new Specialty
                {
                    Code = code, NameTh = nameTh, Category = category,
                    SortOrder = sort, IsActive = true,
                });
            }
        }
        await db.SaveChangesAsync();
    }
}
