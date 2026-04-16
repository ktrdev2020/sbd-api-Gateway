using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Gateway.Models;

[Table("CacheDefinitions")]
public class CacheDefinition
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string CacheKeyPattern { get; set; } = "";

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    [Required, MaxLength(100)]
    public string GroupPrefix { get; set; } = "";

    /// <summary>Redis DB index this definition applies to (0, 1, 2, …)</summary>
    public int DbIndex { get; set; } = 0;

    public int? SuggestedTtlMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
