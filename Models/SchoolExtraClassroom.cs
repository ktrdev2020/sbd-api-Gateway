namespace Gateway.Models;

/// <summary>
/// Plan #111 U5 — a classroom the school defines itself, e.g. ห้องเรียน IEP or a
/// parallel room DMC does not report.
///
/// The homeroom screen normally derives its room list from DMC enrolments
/// (grouped by grade + classroom number). That is right for regular classes but
/// leaves a school unable to assign an advisor to any room DMC does not know
/// about — reported by a SchoolAdmin who asked for "ปุ่มเพิ่มห้องเรียน เช่น
/// ห้องเรียน IEP".
///
/// These rows are merged with the DMC-derived list at read time and never
/// override it: if DMC later reports the same (grade, room), the DMC row wins
/// and carries the real pupil count.
/// </summary>
public class SchoolExtraClassroom
{
    public long Id { get; set; }
    public string SchoolCode { get; set; } = "";
    public short AcademicYear { get; set; }
    public long GradeLevelId { get; set; }
    public string? GradeName { get; set; }
    public int LevelOrder { get; set; }
    public short ClassroomNumber { get; set; }
    /// <summary>Optional display suffix, e.g. "IEP" → shown as "ป.1/3 (IEP)".</summary>
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
}
