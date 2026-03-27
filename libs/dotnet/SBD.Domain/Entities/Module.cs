namespace SBD.Domain.Entities;

public class Module
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string Category { get; set; } = "Feature";
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool AssignableToTeacher { get; set; }
    public bool AssignableToStudent { get; set; }
    public string? Icon { get; set; }
    public string? RoutePath { get; set; }
    public int SortOrder { get; set; }

    // --- New fields for Module Management Enhancement ---
    /// <summary>
    /// Comma-separated visibility levels: "public,student,teacher,school,area,superadmin"
    /// </summary>
    public string VisibilityLevels { get; set; } = "school";

    /// <summary>
    /// External URL or internal route for the module entry point.
    /// </summary>
    public string? EntryUrl { get; set; }

    /// <summary>
    /// Path to uploaded module bundle in MinIO storage.
    /// </summary>
    public string? BundlePath { get; set; }

    /// <summary>
    /// Module-specific configuration stored as JSON.
    /// </summary>
    public string? ConfigJson { get; set; }

    /// <summary>
    /// How the module is registered: "internal" | "external_url" | "uploaded"
    /// </summary>
    public string RegistrationType { get; set; } = "internal";

    public string? Author { get; set; }
    public string? License { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ICollection<SchoolModule> SchoolModules { get; set; } = new List<SchoolModule>();
    public ICollection<AreaModuleAssignment> AreaModuleAssignments { get; set; } = new List<AreaModuleAssignment>();
}
