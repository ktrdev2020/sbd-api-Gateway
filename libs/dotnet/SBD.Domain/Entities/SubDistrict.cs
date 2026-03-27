namespace SBD.Domain.Entities;

public class SubDistrict
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameTh { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? PostalCode { get; set; }
    public int DistrictId { get; set; }

    public District District { get; set; } = null!;
}
