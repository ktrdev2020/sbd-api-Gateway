using Gateway.Models;
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

    public DbSet<CacheDefinition> CacheDefinitions => Set<CacheDefinition>();

    // ── Plan #26 — School profile expansion ──
    public DbSet<SchoolIdentity> SchoolIdentities => Set<SchoolIdentity>();
    public DbSet<SchoolGradeStat> SchoolGradeStats => Set<SchoolGradeStat>();
    public DbSet<SchoolPersonnelTypeStat> SchoolPersonnelTypeStats => Set<SchoolPersonnelTypeStat>();
    // ── Plan #26 Phase 3 — Community + Villages ──
    public DbSet<Village> Villages => Set<Village>();
    public DbSet<SchoolCommunityContext> SchoolCommunityContexts => Set<SchoolCommunityContext>();
    public DbSet<SchoolServiceVillage> SchoolServiceVillages => Set<SchoolServiceVillage>();
    public DbSet<SchoolMajorOccupation> SchoolMajorOccupations => Set<SchoolMajorOccupation>();
    public DbSet<SchoolBoardMember> SchoolBoardMembers => Set<SchoolBoardMember>();
    // ── Plan #27 Phase A.0 — Teacher homeroom advisor assignments ──
    public DbSet<TeacherHomeroomAssignment> TeacherHomeroomAssignments => Set<TeacherHomeroomAssignment>();
    // ── Plan #46 — งานภายใต้แต่ละฝ่ายงาน (School Org Structure) ──
    public DbSet<SchoolOrgTask> SchoolOrgTasks => Set<SchoolOrgTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Plan #26 — School profile expansion shadow properties on School ──
        modelBuilder.Entity<School>(entity =>
        {
            entity.Property<string?>("History").HasColumnName("History");
            entity.Property<int?>("LandRai").HasColumnName("LandRai");
            entity.Property<int?>("LandNgan").HasColumnName("LandNgan");
            entity.Property<decimal?>("LandSqwa").HasColumnType("numeric(8,2)").HasColumnName("LandSqwa");

            // ── Plan #31 — Manual tier checklist (school-declared source of truth) ──
            entity.Property<bool?>("TeachesPreschool").HasColumnName("TeachesPreschool");
            entity.Property<bool?>("TeachesPrimary").HasColumnName("TeachesPrimary");
            entity.Property<bool?>("TeachesLowerSecondary").HasColumnName("TeachesLowerSecondary");
            entity.Property<bool?>("TeachesUpperSecondary").HasColumnName("TeachesUpperSecondary");
        });

        // ── Plan #27 — Personnel.CoverPhoto (shadow property; raw-SQL migrated) ──
        modelBuilder.Entity<Personnel>(entity =>
        {
            entity.Property<string?>("CoverPhoto").HasColumnName("CoverPhoto");
        });

        // ── Plan #26 — SchoolIdentity ──
        modelBuilder.Entity<SchoolIdentity>(entity =>
        {
            entity.ToTable("school_identities");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SchoolCode).HasColumnName("school_code").HasMaxLength(10).IsRequired();
            entity.Property(e => e.FiscalYear).HasColumnName("fiscal_year");
            entity.Property(e => e.Vision).HasColumnName("vision");
            entity.Property(e => e.Philosophy).HasColumnName("philosophy");
            entity.Property(e => e.Slogan).HasColumnName("slogan").HasMaxLength(255);
            entity.Property(e => e.Abbreviation).HasColumnName("abbreviation").HasMaxLength(50);
            entity.Property(e => e.SchoolColors).HasColumnName("school_colors").HasMaxLength(255);
            entity.Property(e => e.SchoolTree).HasColumnName("school_tree").HasMaxLength(255);
            entity.Property(e => e.SchoolFlower).HasColumnName("school_flower").HasMaxLength(255);
            entity.Property(e => e.UniformDescription).HasColumnName("uniform_description");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            entity.HasIndex(e => new { e.SchoolCode, e.FiscalYear }).IsUnique();

            entity.HasMany(e => e.Missions).WithOne()
                .HasForeignKey(m => m.IdentityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Goals).WithOne()
                .HasForeignKey(g => g.IdentityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Strategies).WithOne()
                .HasForeignKey(s => s.IdentityId).OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureIdentityListItem<SchoolMission>(modelBuilder, "school_missions");
        ConfigureIdentityListItem<SchoolGoal>(modelBuilder, "school_goals");
        ConfigureIdentityListItem<SchoolStrategy>(modelBuilder, "school_strategies");

        // ── Plan #26 — SchoolGradeStat ──
        modelBuilder.Entity<SchoolGradeStat>(entity =>
        {
            entity.ToTable("school_grade_stats");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SchoolCode).HasColumnName("school_code").HasMaxLength(10).IsRequired();
            entity.Property(e => e.AcademicYear).HasColumnName("academic_year");
            entity.Property(e => e.Grade).HasColumnName("grade").HasMaxLength(20).IsRequired();
            entity.Property(e => e.GradeOrder).HasColumnName("grade_order");
            entity.Property(e => e.MaleCount).HasColumnName("male_count");
            entity.Property(e => e.FemaleCount).HasColumnName("female_count");
            entity.Property(e => e.ClassroomCount).HasColumnName("classroom_count");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            entity.HasIndex(e => new { e.SchoolCode, e.AcademicYear, e.Grade }).IsUnique();
        });

        // ── Plan #26 — SchoolPersonnelTypeStat ──
        modelBuilder.Entity<SchoolPersonnelTypeStat>(entity =>
        {
            entity.ToTable("school_personnel_type_stats");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SchoolCode).HasColumnName("school_code").HasMaxLength(10).IsRequired();
            entity.Property(e => e.AcademicYear).HasColumnName("academic_year");
            entity.Property(e => e.PersonnelType).HasColumnName("personnel_type").HasMaxLength(50).IsRequired();
            entity.Property(e => e.TypeOrder).HasColumnName("type_order");
            entity.Property(e => e.MaleCount).HasColumnName("male_count");
            entity.Property(e => e.FemaleCount).HasColumnName("female_count");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            entity.HasIndex(e => new { e.SchoolCode, e.AcademicYear, e.PersonnelType }).IsUnique();
        });

        // ── Plan #26 Phase 3 — Villages master ──
        modelBuilder.Entity<Village>(entity =>
        {
            entity.ToTable("Villages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.SubDistrictId).HasColumnName("SubDistrictId");
            entity.Property(e => e.MooNo).HasColumnName("MooNo");
            entity.Property(e => e.NameTh).HasColumnName("NameTh").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Code).HasColumnName("Code").HasMaxLength(20);
            entity.Property(e => e.IsActive).HasColumnName("IsActive");
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasDefaultValueSql("NOW()");
            entity.HasIndex(e => new { e.SubDistrictId, e.MooNo }).IsUnique();
            entity.HasIndex(e => e.Code);
        });

        // ── Plan #26 Phase 3 — SchoolCommunityContext ──
        modelBuilder.Entity<SchoolCommunityContext>(entity =>
        {
            entity.ToTable("school_community_context");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SchoolCode).HasColumnName("school_code").HasMaxLength(10).IsRequired();
            entity.Property(e => e.FiscalYear).HasColumnName("fiscal_year");
            entity.Property(e => e.SubdistrictMalePopulation).HasColumnName("subdistrict_male_population");
            entity.Property(e => e.SubdistrictFemalePopulation).HasColumnName("subdistrict_female_population");
            entity.Property(e => e.SubdistrictHouseholdCount).HasColumnName("subdistrict_household_count");
            entity.Property(e => e.GeographyDescription).HasColumnName("geography_description");
            entity.Property(e => e.ClimateDescription).HasColumnName("climate_description");
            entity.Property(e => e.EconomyDescription).HasColumnName("economy_description");
            entity.Property(e => e.ReligionCultureDescription).HasColumnName("religion_culture_description");
            entity.Property(e => e.AverageIncomePerHousehold).HasColumnName("average_income_per_household").HasColumnType("numeric(12,2)");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            entity.HasIndex(e => new { e.SchoolCode, e.FiscalYear }).IsUnique();

            entity.HasMany(e => e.ServiceVillages).WithOne()
                .HasForeignKey(v => v.ContextId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Occupations).WithOne()
                .HasForeignKey(o => o.ContextId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SchoolServiceVillage>(entity =>
        {
            entity.ToTable("school_service_villages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ContextId).HasColumnName("context_id");
            entity.Property(e => e.VillageId).HasColumnName("village_id");
            entity.Property(e => e.HeadmanName).HasColumnName("headman_name").HasMaxLength(255);
            entity.Property(e => e.HeadmanPhone).HasColumnName("headman_phone").HasMaxLength(50);
            entity.Property(e => e.MaleCount).HasColumnName("male_count");
            entity.Property(e => e.FemaleCount).HasColumnName("female_count");
            entity.Property(e => e.HouseholdCount).HasColumnName("household_count");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.Notes).HasColumnName("notes");
        });

        modelBuilder.Entity<SchoolMajorOccupation>(entity =>
        {
            entity.ToTable("school_major_occupations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ContextId).HasColumnName("context_id");
            entity.Property(e => e.OccupationName).HasColumnName("occupation_name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.HouseholdCount).HasColumnName("household_count");
            entity.Property(e => e.Percentage).HasColumnName("percentage").HasColumnType("numeric(5,2)");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
        });

        modelBuilder.Entity<SchoolBoardMember>(entity =>
        {
            entity.ToTable("school_board_members");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SchoolCode).HasColumnName("school_code").HasMaxLength(10).IsRequired();
            entity.Property(e => e.FiscalYear).HasColumnName("fiscal_year");
            entity.Property(e => e.MemberName).HasColumnName("member_name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(100);
            entity.Property(e => e.Representing).HasColumnName("representing").HasMaxLength(100);
            entity.Property(e => e.ContactPhone).HasColumnName("contact_phone").HasMaxLength(50);
            entity.Property(e => e.AppointedAt).HasColumnName("appointed_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.HasIndex(e => new { e.SchoolCode, e.FiscalYear });
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

        // SchoolModule: IsPilot + Notes are now real entity properties in SBD.Domain.
        // Shadow property definitions removed — EF Core uses the entity CLR properties directly.

        // ── Plan #27 Phase A.0 — TeacherHomeroomAssignment ──
        modelBuilder.Entity<TeacherHomeroomAssignment>(entity =>
        {
            entity.ToTable("TeacherHomeroomAssignments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SchoolCode).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(20).HasDefaultValue("advisor");
            entity.Property(e => e.AssignedAt).HasDefaultValueSql("NOW()");
            entity.HasIndex(e => new { e.PersonnelId, e.SchoolCode, e.AcademicYear, e.GradeLevelId, e.ClassroomNumber })
                .HasDatabaseName("UX_teacher_homeroom_active")
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.SchoolCode, e.AcademicYear })
                .HasDatabaseName("IX_teacher_homeroom_school_year")
                .HasFilter("\"DeletedAt\" IS NULL");
        });

        // ── Plan #46 — SchoolOrgTask (งานในฝ่ายงานของโรงเรียน) ──
        modelBuilder.Entity<SchoolOrgTask>(entity =>
        {
            entity.ToTable("school_org_tasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkGroupId).HasColumnName("work_group_id");
            entity.Property(e => e.NameTh).HasColumnName("name_th").HasMaxLength(500).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(e => new { e.WorkGroupId, e.SortOrder });
            entity.HasOne<WorkGroup>()
                .WithMany()
                .HasForeignKey(e => e.WorkGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureIdentityListItem<T>(ModelBuilder modelBuilder, string tableName) where T : class
    {
        modelBuilder.Entity<T>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey("Id");
            entity.Property<long>("Id").HasColumnName("id");
            entity.Property<long>("IdentityId").HasColumnName("identity_id");
            entity.Property<int>("SortOrder").HasColumnName("sort_order");
            entity.Property<string>("Description").HasColumnName("description").IsRequired();
        });
    }
}
