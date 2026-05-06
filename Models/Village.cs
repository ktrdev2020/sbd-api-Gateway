namespace Gateway.Models;

/// <summary>
/// Plan #26 Phase 3 — Village master (หมู่บ้าน) keyed by DOPA 8-digit code
/// (PPDDSSMM = province/district/subdistrict/moo) for cross-reference with
/// กรมการปกครอง household-registration data (ทร.14) in future GIS imports.
/// SubDistrictId is the FK to SBD's existing SubDistricts; MooNo is the หมู่ที่ within it.
/// </summary>
public class Village
{
    public int Id { get; set; }
    public int SubDistrictId { get; set; }
    public int MooNo { get; set; }
    public required string NameTh { get; set; }
    public string? Code { get; set; }   // DOPA 8-digit, nullable until backfilled
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
