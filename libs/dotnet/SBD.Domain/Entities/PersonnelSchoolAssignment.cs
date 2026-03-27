namespace SBD.Domain.Entities;

public class PersonnelSchoolAssignment
{
    public int Id { get; set; }
    public int PersonnelId { get; set; }
    public int SchoolId { get; set; }
    public string? Position { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Personnel Personnel { get; set; } = null!;
    public School School { get; set; } = null!;
}
