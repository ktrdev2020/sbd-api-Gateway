namespace SBD.Domain.Entities;

public class AreaModuleAssignment
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public int ModuleId { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether schools in this area can self-enable this module.
    /// </summary>
    public bool AllowSchoolSelfEnable { get; set; }

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? AssignedBy { get; set; }
    public string? Notes { get; set; }

    // Navigation properties
    public Area Area { get; set; } = null!;
    public Module Module { get; set; } = null!;
}
