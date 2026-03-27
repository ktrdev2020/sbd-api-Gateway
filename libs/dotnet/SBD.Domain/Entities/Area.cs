namespace SBD.Domain.Entities;

public class Area
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameTh { get; set; } = string.Empty;
    public int AreaTypeId { get; set; }
    public int? ProvinceId { get; set; }

    // Navigation properties
    public AreaType AreaType { get; set; } = null!;
    public Province? Province { get; set; }
    public ICollection<School> Schools { get; set; } = new List<School>();
    public ICollection<AreaModuleAssignment> AreaModuleAssignments { get; set; } = new List<AreaModuleAssignment>();
}
