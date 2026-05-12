namespace Gateway.Models;

/// <summary>
/// Plan #46 — งาน (task) ภายใต้แต่ละฝ่ายงาน (WorkGroup ScopeType="School")
/// เช่น ภายใต้ "บริหารงานวิชาการ" มี: "การวางแผนงานด้านวิชาการ", "การจัดการเรียนการสอนในสถานศึกษา"...
/// </summary>
public class SchoolOrgTask
{
    public long Id { get; set; }

    /// <summary>FK to WorkGroup.Id (ScopeType must be "School")</summary>
    public int WorkGroupId { get; set; }

    public required string NameTh { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
