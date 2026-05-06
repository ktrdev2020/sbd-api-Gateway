namespace Gateway.Models;

/// <summary>
/// Plan #26 Phase 3 — Community/subdistrict context per school × fiscal year.
/// Source: aplan PDF บทที่ 2.2-2.7 (สภาพชุมชน · เขตบริการ · เศรษฐกิจ · ประเพณี).
/// </summary>
public class SchoolCommunityContext
{
    public long Id { get; set; }
    public required string SchoolCode { get; set; }
    public int FiscalYear { get; set; }
    public int? SubdistrictMalePopulation { get; set; }
    public int? SubdistrictFemalePopulation { get; set; }
    public int? SubdistrictHouseholdCount { get; set; }
    public string? GeographyDescription { get; set; }
    public string? ClimateDescription { get; set; }
    public string? EconomyDescription { get; set; }
    public string? ReligionCultureDescription { get; set; }
    public decimal? AverageIncomePerHousehold { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SchoolServiceVillage> ServiceVillages { get; set; } = new List<SchoolServiceVillage>();
    public ICollection<SchoolMajorOccupation> Occupations { get; set; } = new List<SchoolMajorOccupation>();
}

/// <summary>เขตบริการ: village FK + per-context counts/headman</summary>
public class SchoolServiceVillage
{
    public long Id { get; set; }
    public long ContextId { get; set; }
    public int VillageId { get; set; }              // FK to Villages master
    public string? HeadmanName { get; set; }
    public string? HeadmanPhone { get; set; }
    public int? MaleCount { get; set; }
    public int? FemaleCount { get; set; }
    public int? HouseholdCount { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
}

/// <summary>อาชีพหลักในชุมชน</summary>
public class SchoolMajorOccupation
{
    public long Id { get; set; }
    public long ContextId { get; set; }
    public required string OccupationName { get; set; }
    public int? HouseholdCount { get; set; }
    public decimal? Percentage { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>คณะกรรมการสถานศึกษา per fiscal year</summary>
public class SchoolBoardMember
{
    public long Id { get; set; }
    public required string SchoolCode { get; set; }
    public int FiscalYear { get; set; }
    public required string MemberName { get; set; }
    public string? Role { get; set; }                // ประธาน/รองประธาน/กรรมการ/เลขานุการ
    public string? Representing { get; set; }        // ผู้ทรงคุณวุฒิ/ผู้ปกครอง/ครู/ชุมชน/พระ/ศิษย์เก่า
    public string? ContactPhone { get; set; }
    public DateOnly? AppointedAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
