using Gateway.Data.Entities;
using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway.Data;

/// <summary>
/// Extends SbdDbContext with Gateway-specific entities and shadow properties
/// for new Module/SchoolModule fields that will be added to NuGet packages later.
///
/// Strategy:
/// - AreaModuleAssignment: new local entity (will migrate to SBD.Domain in next NuGet release)
/// - Module shadow properties: VisibilityLevels, RegistrationType, EntryUrl, BundlePath,
///   ConfigJson, Author, License, CreatedAt, UpdatedAt
/// - SchoolModule shadow properties: Notes, IsPilot
///
/// When SBD.Domain NuGet is updated with these fields, remove shadow property
/// definitions here — EF Core will use the entity properties directly.
/// </summary>
public class GatewayDbContext : SbdDbContext
{
    public GatewayDbContext(DbContextOptions<SbdDbContext> options) : base(options) { }

    public DbSet<AreaModuleAssignment> AreaModuleAssignments => Set<AreaModuleAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── AreaModuleAssignment (new entity, local to Gateway) ──
        modelBuilder.Entity<AreaModuleAssignment>(entity =>
        {
            entity.ToTable("AreaModuleAssignments");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AreaId, e.ModuleId }).IsUnique();
            entity.HasOne(e => e.Area).WithMany().HasForeignKey(e => e.AreaId);
            entity.HasOne(e => e.Module).WithMany().HasForeignKey(e => e.ModuleId);
        });

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

        // ── SchoolModule: shadow properties for new fields not yet in NuGet ──
        modelBuilder.Entity<SchoolModule>(entity =>
        {
            entity.Property<string?>("Notes")
                .HasColumnName("Notes");
            entity.Property<bool>("IsPilot")
                .HasDefaultValue(false).HasColumnName("IsPilot");
        });
    }
}
