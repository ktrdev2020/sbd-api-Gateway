namespace SBD.Domain.Entities;

public class SchoolModule
{
    public int Id { get; set; }
    public int SchoolId { get; set; }
    public int ModuleId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;

    // --- New fields for Module Management Enhancement ---
    public string? Notes { get; set; }
    public bool IsPilot { get; set; }

    // Navigation properties
    public School School { get; set; } = null!;
    public Module Module { get; set; } = null!;
    public ICollection<TeacherModuleAssignment> TeacherAssignments { get; set; } = new List<TeacherModuleAssignment>();
}
