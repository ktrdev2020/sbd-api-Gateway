namespace Gateway.Models;

/// <summary>
/// Plan #26 — Student count per grade × academic year.
/// Source: aplan PDF บทที่ 1 (สภาพปัจจุบัน — ข้อมูลนักเรียน).
/// </summary>
public class SchoolGradeStat
{
    public long Id { get; set; }
    public required string SchoolCode { get; set; }
    public int AcademicYear { get; set; }
    public required string Grade { get; set; }   // "อ.1","อ.2","อ.3","ป.1"-"ป.6","ม.1"-"ม.6"
    public int GradeOrder { get; set; }
    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }
    public int? ClassroomCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Plan #26 — Personnel count per type × academic year.
/// Source: aplan PDF บทที่ 1 (สภาพปัจจุบัน — ข้อมูลบุคลากร).
/// </summary>
public class SchoolPersonnelTypeStat
{
    public long Id { get; set; }
    public required string SchoolCode { get; set; }
    public int AcademicYear { get; set; }
    public required string PersonnelType { get; set; }  // "ผู้อำนวยการ","รองผู้อำนวยการ","ครู","พนักงานราชการ","ลูกจ้างประจำ","ลูกจ้างชั่วคราว","ธุรการ","นักการภารโรง"
    public int TypeOrder { get; set; }
    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
