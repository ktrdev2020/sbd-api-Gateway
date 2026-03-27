namespace SBD.Domain.Entities;

public class AreaType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameTh { get; set; } = string.Empty;
    public string? NameShortTh { get; set; }
    public string? NameEn { get; set; }
    public int Level { get; set; }

    public ICollection<Area> Areas { get; set; } = new List<Area>();
}
