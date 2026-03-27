namespace SBD.Domain.Entities;

public class Country
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameTh { get; set; } = string.Empty;
    public string? NameEn { get; set; }

    public ICollection<Province> Provinces { get; set; } = new List<Province>();
}
