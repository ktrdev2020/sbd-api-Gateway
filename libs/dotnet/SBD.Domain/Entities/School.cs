namespace SBD.Domain.Entities;

public class School
{
    public int Id { get; set; }
    public string SchoolCode { get; set; } = string.Empty;
    public string NameTh { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int? AreaId { get; set; }
    public int? AreaTypeId { get; set; }
    public string? SchoolCluster { get; set; }
    public string? SchoolLevel { get; set; }
    public string? SchoolType { get; set; }
    public string? SchoolSizeStd4 { get; set; }
    public string? SchoolSizeStd7 { get; set; }
    public string? SchoolSizeHr { get; set; }
    public string? Principal { get; set; }
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? TaxId { get; set; }
    public string? LogoUrl { get; set; }
    public string? SmisCode { get; set; }
    public string? PerCode { get; set; }
    public DateOnly? EstablishedDate { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? StudentCount { get; set; }
    public int? TeacherCount { get; set; }
    public int? AddressId { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Area? Area { get; set; }
    public AreaType? AreaType { get; set; }
    public Address? Address { get; set; }
    public ICollection<SchoolModule> SchoolModules { get; set; } = new List<SchoolModule>();
    public ICollection<PersonnelSchoolAssignment> PersonnelAssignments { get; set; } = new List<PersonnelSchoolAssignment>();
}
