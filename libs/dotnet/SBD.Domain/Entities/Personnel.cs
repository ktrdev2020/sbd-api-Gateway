namespace SBD.Domain.Entities;

public class Personnel
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int? TitlePrefixId { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public TitlePrefix? TitlePrefix { get; set; }
    public ICollection<PersonnelSchoolAssignment> SchoolAssignments { get; set; } = new List<PersonnelSchoolAssignment>();
    public ICollection<TeacherModuleAssignment> ModuleAssignments { get; set; } = new List<TeacherModuleAssignment>();
}
