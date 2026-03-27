namespace SBD.Application.DTOs;

public class SchoolDto
{
    public int Id { get; set; }
    public string SchoolCode { get; set; } = string.Empty;
    public string? SmisCode { get; set; }
    public string? PerCode { get; set; }
    public string NameTh { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int? AreaId { get; set; }
    public string? AreaName { get; set; }
    public string? AreaTypeName { get; set; }
    public int? AreaTypeId { get; set; }
    public string? SchoolCluster { get; set; }
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? TaxId { get; set; }
    public string? SchoolType { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? SchoolSizeStd4 { get; set; }
    public string? SchoolSizeStd7 { get; set; }
    public string? SchoolSizeHr { get; set; }
    public string? SchoolLevel { get; set; }
    public string? Principal { get; set; }
    public DateOnly? EstablishedDate { get; set; }
    public bool IsActive { get; set; }
    public int? StudentCount { get; set; }
    public int? TeacherCount { get; set; }
    public string? LogoUrl { get; set; }
    public AddressDto? Address { get; set; }
}

public class AddressDto
{
    public string? HouseNumber { get; set; }
    public string? VillageNo { get; set; }
    public string? VillageName { get; set; }
    public string? Road { get; set; }
    public string? Soi { get; set; }
    public string? SubDistrictName { get; set; }
    public string? DistrictName { get; set; }
    public string? ProvinceName { get; set; }
    public string? PostalCode { get; set; }
}
