namespace SBD.Domain.Entities;

public class Address
{
    public int Id { get; set; }
    public string? HouseNumber { get; set; }
    public string? VillageNo { get; set; }
    public string? VillageName { get; set; }
    public string? Road { get; set; }
    public string? Soi { get; set; }
    public int? SubDistrictId { get; set; }

    public SubDistrict? SubDistrict { get; set; }
    public School? School { get; set; }
}
