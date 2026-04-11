using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Data;

/// <summary>
/// Extends SbdDbContext with Gateway-specific entities and shadow properties
/// for new Module/SchoolModule fields that will be added to NuGet packages later.
///
/// Strategy:
/// - Module shadow properties: VisibilityLevels, RegistrationType, EntryUrl, BundlePath,
///   ConfigJson, Author, License, CreatedAt, UpdatedAt
///
/// SchoolModule.IsPilot and SchoolModule.Notes are now real entity properties in SBD.Domain.
/// Their shadow property definitions have been removed.
/// </summary>
public class GatewayDbContext : SbdDbContext
{
    public GatewayDbContext(DbContextOptions<SbdDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Module: shadow properties for new fields not yet in NuGet ──
        // These create DB columns that EF Core manages via EF.Property<T>()
        modelBuilder.Entity<Module>(entity =>
        {
            entity.Property<string>("VisibilityLevels")
                .HasMaxLength(200).HasDefaultValue("school").HasColumnName("VisibilityLevels");
            entity.Property<string?>("EntryUrl")
                .HasColumnName("EntryUrl");
            entity.Property<string?>("BundlePath")
                .HasColumnName("BundlePath");
            entity.Property<string?>("ConfigJson")
                .HasColumnName("ConfigJson");
            entity.Property<string>("RegistrationType")
                .HasMaxLength(50).HasDefaultValue("internal").HasColumnName("RegistrationType");
            entity.Property<string?>("Author")
                .HasMaxLength(200).HasColumnName("Author");
            entity.Property<string?>("License")
                .HasMaxLength(100).HasColumnName("License");
            entity.Property<DateTimeOffset>("CreatedAt")
                .HasDefaultValueSql("NOW()").HasColumnName("CreatedAt");
            entity.Property<DateTimeOffset>("UpdatedAt")
                .HasDefaultValueSql("NOW()").HasColumnName("UpdatedAt");
        });

        // SchoolModule: IsPilot + Notes are now real entity properties in SBD.Domain.
        // Shadow property definitions removed — EF Core uses the entity CLR properties directly.
    }
}
