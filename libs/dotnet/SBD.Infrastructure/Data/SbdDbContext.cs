using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;

namespace SBD.Infrastructure.Data;

public class SbdDbContext : DbContext
{
    public SbdDbContext(DbContextOptions<SbdDbContext> options) : base(options) { }

    // Core entities
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<School> Schools => Set<School>();
    public DbSet<SchoolModule> SchoolModules => Set<SchoolModule>();
    public DbSet<AreaModuleAssignment> AreaModuleAssignments => Set<AreaModuleAssignment>();
    public DbSet<TeacherModuleAssignment> TeacherModuleAssignments => Set<TeacherModuleAssignment>();
    public DbSet<Personnel> Personnel => Set<Personnel>();
    public DbSet<PersonnelSchoolAssignment> PersonnelSchoolAssignments => Set<PersonnelSchoolAssignment>();

    // Geographic entities
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<SubDistrict> SubDistricts => Set<SubDistrict>();
    public DbSet<Address> Addresses => Set<Address>();

    // Reference data
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<AreaType> AreaTypes => Set<AreaType>();
    public DbSet<TitlePrefix> TitlePrefixes => Set<TitlePrefix>();
    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Module
        modelBuilder.Entity<Module>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Category).HasMaxLength(50);
            entity.Property(e => e.Version).HasMaxLength(20);
            entity.Property(e => e.VisibilityLevels).HasMaxLength(200).HasDefaultValue("school");
            entity.Property(e => e.RegistrationType).HasMaxLength(50).HasDefaultValue("internal");
            entity.Property(e => e.Author).HasMaxLength(200);
            entity.Property(e => e.License).HasMaxLength(100);
        });

        // SchoolModule
        modelBuilder.Entity<SchoolModule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SchoolId, e.ModuleId }).IsUnique();
            entity.HasOne(e => e.School).WithMany(s => s.SchoolModules).HasForeignKey(e => e.SchoolId);
            entity.HasOne(e => e.Module).WithMany(m => m.SchoolModules).HasForeignKey(e => e.ModuleId);
        });

        // AreaModuleAssignment
        modelBuilder.Entity<AreaModuleAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AreaId, e.ModuleId }).IsUnique();
            entity.HasOne(e => e.Area).WithMany(a => a.AreaModuleAssignments).HasForeignKey(e => e.AreaId);
            entity.HasOne(e => e.Module).WithMany(m => m.AreaModuleAssignments).HasForeignKey(e => e.ModuleId);
        });

        // TeacherModuleAssignment
        modelBuilder.Entity<TeacherModuleAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TeacherId, e.SchoolModuleId }).IsUnique();
            entity.HasOne(e => e.Teacher).WithMany(p => p.ModuleAssignments).HasForeignKey(e => e.TeacherId);
            entity.HasOne(e => e.SchoolModule).WithMany(sm => sm.TeacherAssignments).HasForeignKey(e => e.SchoolModuleId);
        });

        // PersonnelSchoolAssignment
        modelBuilder.Entity<PersonnelSchoolAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PersonnelId, e.SchoolId }).IsUnique();
            entity.HasOne(e => e.Personnel).WithMany(p => p.SchoolAssignments).HasForeignKey(e => e.PersonnelId);
            entity.HasOne(e => e.School).WithMany(s => s.PersonnelAssignments).HasForeignKey(e => e.SchoolId);
        });

        // School
        modelBuilder.Entity<School>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SchoolCode).IsUnique();
            entity.Property(e => e.SchoolCode).IsRequired().HasMaxLength(20);
            entity.Property(e => e.NameTh).IsRequired().HasMaxLength(200);
            entity.HasOne(e => e.Area).WithMany(a => a.Schools).HasForeignKey(e => e.AreaId);
            entity.HasOne(e => e.AreaType).WithMany().HasForeignKey(e => e.AreaTypeId);
            entity.HasOne(e => e.Address).WithOne(a => a.School).HasForeignKey<School>(s => s.AddressId);
        });

        // Personnel
        modelBuilder.Entity<Personnel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.TitlePrefix).WithMany().HasForeignKey(e => e.TitlePrefixId);
        });

        // Area
        modelBuilder.Entity<Area>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(20);
            entity.Property(e => e.NameTh).IsRequired().HasMaxLength(200);
            entity.HasOne(e => e.AreaType).WithMany(at => at.Areas).HasForeignKey(e => e.AreaTypeId);
            entity.HasOne(e => e.Province).WithMany().HasForeignKey(e => e.ProvinceId);
        });

        // AreaType
        modelBuilder.Entity<AreaType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(20);
            entity.Property(e => e.NameTh).IsRequired().HasMaxLength(100);
        });

        // Geographic hierarchy
        modelBuilder.Entity<Country>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(10);
            entity.Property(e => e.NameTh).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<Province>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(10);
            entity.Property(e => e.NameTh).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.Country).WithMany(c => c.Provinces).HasForeignKey(e => e.CountryId);
        });

        modelBuilder.Entity<District>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(10);
            entity.Property(e => e.NameTh).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.Province).WithMany(p => p.Districts).HasForeignKey(e => e.ProvinceId);
        });

        modelBuilder.Entity<SubDistrict>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(10);
            entity.Property(e => e.NameTh).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.District).WithMany(d => d.SubDistricts).HasForeignKey(e => e.DistrictId);
        });

        // Address
        modelBuilder.Entity<Address>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.SubDistrict).WithMany().HasForeignKey(e => e.SubDistrictId);
        });

        // TitlePrefix
        modelBuilder.Entity<TitlePrefix>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NameTh).IsRequired().HasMaxLength(50);
        });

        // Role
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.NameTh).IsRequired().HasMaxLength(100);
        });
    }
}
