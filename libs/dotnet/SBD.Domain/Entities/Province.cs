namespace SBD.Domain.Entities;

public class Province
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameTh { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Region { get; set; }
    public int CountryId { get; set; }

    public Country Country { get; set; } = null!;
    public ICollection<District> Districts { get; set; } = new List<District>();
}
