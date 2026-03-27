namespace SBD.Domain.Entities;

public class District
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameTh { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int ProvinceId { get; set; }

    public Province Province { get; set; } = null!;
    public ICollection<SubDistrict> SubDistricts { get; set; } = new List<SubDistrict>();
}
