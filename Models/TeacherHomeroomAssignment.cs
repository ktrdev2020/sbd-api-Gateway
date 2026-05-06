namespace Gateway.Models;

public class TeacherHomeroomAssignment
{
    public long Id { get; set; }
    public int PersonnelId { get; set; }
    public required string SchoolCode { get; set; }
    public short AcademicYear { get; set; }
    public short? Term { get; set; }
    public long GradeLevelId { get; set; }
    public short ClassroomNumber { get; set; }
    public string Role { get; set; } = "advisor";
    public int AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
}
