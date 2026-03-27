namespace SBD.Domain.Entities;

public class TeacherModuleAssignment
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public int SchoolModuleId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Personnel Teacher { get; set; } = null!;
    public SchoolModule SchoolModule { get; set; } = null!;
}
