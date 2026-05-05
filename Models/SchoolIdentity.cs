namespace Gateway.Models;

/// <summary>
/// Plan #26 — School identity (วิสัยทัศน์ / ปรัชญา / คำขวัญ / อักษรย่อ / สี / ต้นไม้ / ดอกไม้ / ชุดนักเรียน)
/// versioned per fiscal_year. Source: aplan PDF บทที่ 3.
/// </summary>
public class SchoolIdentity
{
    public long Id { get; set; }
    public required string SchoolCode { get; set; }
    public int FiscalYear { get; set; }
    public string? Vision { get; set; }
    public string? Philosophy { get; set; }
    public string? Slogan { get; set; }
    public string? Abbreviation { get; set; }
    public string? SchoolColors { get; set; }
    public string? SchoolTree { get; set; }
    public string? SchoolFlower { get; set; }
    public string? UniformDescription { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SchoolMission> Missions { get; set; } = new List<SchoolMission>();
    public ICollection<SchoolGoal> Goals { get; set; } = new List<SchoolGoal>();
    public ICollection<SchoolStrategy> Strategies { get; set; } = new List<SchoolStrategy>();
}

/// <summary>พันธกิจ — list item per identity.</summary>
public class SchoolMission
{
    public long Id { get; set; }
    public long IdentityId { get; set; }
    public int SortOrder { get; set; }
    public required string Description { get; set; }
}

/// <summary>เป้าประสงค์ — list item per identity.</summary>
public class SchoolGoal
{
    public long Id { get; set; }
    public long IdentityId { get; set; }
    public int SortOrder { get; set; }
    public required string Description { get; set; }
}

/// <summary>ยุทธศาสตร์ — list item per identity.</summary>
public class SchoolStrategy
{
    public long Id { get; set; }
    public long IdentityId { get; set; }
    public int SortOrder { get; set; }
    public required string Description { get; set; }
}
