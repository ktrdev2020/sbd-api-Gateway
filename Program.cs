using System.Text;
using Gateway.Data;
using Gateway.Services;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using SBD.Infrastructure.Data;
using SBD.ServiceRegistry;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add CORS for Angular frontend
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add JWT Bearer Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
var key = Encoding.ASCII.GetBytes(secretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        // Tolerate small clock drift between AuthService and Gateway pods.
        // Zero skew + containerized clocks = spurious 401s on freshly-minted
        // tokens. AuthService uses the same 30s tolerance.
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization();

// Add Redis
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Redis connection string not configured");
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// Add DbContext - GatewayDbContext extends SbdDbContext with local entities + shadow properties
// Register DbContextOptions<SbdDbContext> first (needed by GatewayDbContext constructor),
// then override SbdDbContext resolution to use GatewayDbContext.
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL") ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
builder.Services.AddDbContext<SbdDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
           .ConfigureWarnings(w => w.Ignore(
               Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddScoped<SbdDbContext, GatewayDbContext>();

// Add MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    // Consumers
    x.AddConsumer<Gateway.Consumers.SchoolLogoUpdatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitUri = builder.Configuration.GetConnectionString("RabbitMQ")
            ?? "amqp://guest:guest@localhost:5672";
        cfg.Host(new Uri(rabbitUri));
        cfg.ConfigureEndpoints(context);
    });
});

// Add HttpClient for health checks
builder.Services.AddHttpClient();

// Register in service registry
builder.Services.AddServiceRegistration("Gateway", opts =>
{
    opts.Port = 5000;
    opts.BaseUrl = "http://localhost:5000";
    opts.HealthUrl = "http://localhost:5000/health";
});

// Add Controllers
builder.Services.AddControllers();

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SBD Gateway API",
        Version = "v1",
        Description = "Scalable Education Platform - Gateway API"
    });
});

var app = builder.Build();

// Apply pending migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SbdDbContext>();

    // Apply migrations with automatic baselining for legacy schemas.
    //
    // Context: this DB is shared across multiple migration assemblies (SBD.Infrastructure +
    // the Gateway project itself). When a snapshot-style migration is added to one assembly
    // after the schema was already created by another path (EnsureCreated, raw SQL, or older
    // migrations), MigrateAsync() will fail with PostgreSQL 42P07 ("relation already exists").
    //
    // Strategy: try MigrateAsync; if it trips on 42P07, the offending migration is purely a
    // baseline of pre-existing tables. Mark it as applied (insert into __EFMigrationsHistory)
    // and retry. EF wraps each migration in its own transaction, so the failed CREATE TABLE
    // is rolled back cleanly and the migration is still in the pending list.
    //
    // This loop is bounded so a genuine error (different SqlState, or a migration that keeps
    // failing for the same reason without progress) surfaces instead of spinning forever.
    const string productVersion = "9.0.2";
    const int maxBaselineAttempts = 50;
    var baselined = new HashSet<string>();
    var attempt = 0;

    while (true)
    {
        try
        {
            await db.Database.MigrateAsync();
            Console.WriteLine("[Migration] Database schema is up to date.");
            break;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P07")
        {
            attempt++;
            if (attempt > maxBaselineAttempts)
            {
                Console.WriteLine($"[Migration] Exceeded {maxBaselineAttempts} baseline attempts. Aborting.");
                throw;
            }

            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count == 0)
            {
                // Conflict occurred but EF reports nothing pending — cannot recover.
                throw;
            }

            // EF applies pending migrations in order, so the conflict is on pending[0].
            var failedMigration = pending[0];
            if (!baselined.Add(failedMigration))
            {
                // Already tried to baseline this one and it still failed — bail out to avoid a loop.
                Console.WriteLine($"[Migration] Migration '{failedMigration}' still fails after baselining. Aborting.");
                throw;
            }

            Console.WriteLine($"[Migration] Conflict on '{failedMigration}': {ex.MessageText}");
            Console.WriteLine($"[Migration] Schema already contains the target objects — baselining migration as applied.");

            // Ensure the history table exists (MigrateAsync usually creates it, but be safe in case
            // we're recovering from an even earlier failure).
            await db.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                      ""MigrationId"" character varying(150) NOT NULL,
                      ""ProductVersion"" character varying(32) NOT NULL,
                      CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
                  );");

            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
                  VALUES ({0}, {1})
                  ON CONFLICT (""MigrationId"") DO NOTHING",
                failedMigration, productVersion);

            Console.WriteLine($"[Migration] Baselined '{failedMigration}'. Retrying remaining migrations...");
        }
    }

    // Apply Gateway-specific schema additions (shadow properties + AreaModuleAssignments)
    // These columns are defined as shadow properties in GatewayDbContext but the migration
    // lives in a different assembly, so we apply them via raw SQL (idempotent).
    await db.Database.ExecuteSqlRawAsync(@"
        -- School: soft-delete (recycle bin) columns
        ALTER TABLE ""Schools"" ADD COLUMN IF NOT EXISTS ""DeletedAt"" TIMESTAMPTZ NULL;
        ALTER TABLE ""Schools"" ADD COLUMN IF NOT EXISTS ""DeletedBy"" VARCHAR(255) NULL;
        CREATE INDEX IF NOT EXISTS ""IX_Schools_DeletedAt"" ON ""Schools"" (""DeletedAt"");
        -- School: FileService logo cache (denormalized — kept in sync via SchoolLogoUpdatedConsumer)
        ALTER TABLE ""Schools"" ADD COLUMN IF NOT EXISTS ""LogoThumbnailUrl"" VARCHAR(1000) NULL;
        ALTER TABLE ""Schools"" ADD COLUMN IF NOT EXISTS ""LogoVersion"" INTEGER NOT NULL DEFAULT 0;
        -- Module: shadow property columns
        ALTER TABLE ""Modules"" ADD COLUMN IF NOT EXISTS ""VisibilityLevels"" VARCHAR(200) NOT NULL DEFAULT 'school';
        ALTER TABLE ""Modules"" ADD COLUMN IF NOT EXISTS ""RegistrationType"" VARCHAR(50) NOT NULL DEFAULT 'internal';
        ALTER TABLE ""Modules"" ADD COLUMN IF NOT EXISTS ""EntryUrl"" TEXT;
        ALTER TABLE ""Modules"" ADD COLUMN IF NOT EXISTS ""BundlePath"" TEXT;
        ALTER TABLE ""Modules"" ADD COLUMN IF NOT EXISTS ""ConfigJson"" TEXT;
        ALTER TABLE ""Modules"" ADD COLUMN IF NOT EXISTS ""Author"" VARCHAR(200);
        ALTER TABLE ""Modules"" ADD COLUMN IF NOT EXISTS ""License"" VARCHAR(100);
        ALTER TABLE ""Modules"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW();
        ALTER TABLE ""Modules"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW();
        -- SchoolModule: shadow property columns
        ALTER TABLE ""SchoolModules"" ADD COLUMN IF NOT EXISTS ""Notes"" TEXT;
        ALTER TABLE ""SchoolModules"" ADD COLUMN IF NOT EXISTS ""IsPilot"" BOOLEAN NOT NULL DEFAULT FALSE;
        -- AreaModuleAssignments table
        CREATE TABLE IF NOT EXISTS ""AreaModuleAssignments"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""AreaId"" INTEGER NOT NULL REFERENCES ""Areas""(""Id"") ON DELETE CASCADE,
            ""ModuleId"" INTEGER NOT NULL REFERENCES ""Modules""(""Id"") ON DELETE CASCADE,
            ""IsEnabled"" BOOLEAN NOT NULL DEFAULT TRUE,
            ""AllowSchoolSelfEnable"" BOOLEAN NOT NULL DEFAULT FALSE,
            ""AssignedAt"" TIMESTAMPTZ NOT NULL,
            ""AssignedBy"" INTEGER,
            ""Notes"" TEXT
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AreaModuleAssignments_AreaId_ModuleId""
            ON ""AreaModuleAssignments"" (""AreaId"", ""ModuleId"");
        CREATE INDEX IF NOT EXISTS ""IX_AreaModuleAssignments_ModuleId""
            ON ""AreaModuleAssignments"" (""ModuleId"");
        -- PositionTypes table
        CREATE TABLE IF NOT EXISTS ""PositionTypes"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""Code"" VARCHAR(50) NOT NULL,
            ""NameTh"" VARCHAR(200) NOT NULL,
            ""NameEn"" VARCHAR(200),
            ""Category"" VARCHAR(100) NOT NULL,
            ""IsSchoolDirector"" BOOLEAN NOT NULL DEFAULT FALSE,
            ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
            ""IsActive"" BOOLEAN NOT NULL DEFAULT TRUE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PositionTypes_Code"" ON ""PositionTypes"" (""Code"");

        -- WorkGroups table
        CREATE TABLE IF NOT EXISTS ""WorkGroups"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""Name"" VARCHAR(200) NOT NULL,
            ""Description"" TEXT,
            ""ScopeType"" VARCHAR(50) NOT NULL,
            ""ScopeId"" INTEGER NOT NULL,
            ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
            ""IsActive"" BOOLEAN NOT NULL DEFAULT TRUE
        );
        CREATE INDEX IF NOT EXISTS ""IX_WorkGroups_ScopeType_ScopeId"" ON ""WorkGroups"" (""ScopeType"", ""ScopeId"");

        -- WorkGroupMembers table
        CREATE TABLE IF NOT EXISTS ""WorkGroupMembers"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""WorkGroupId"" INTEGER NOT NULL REFERENCES ""WorkGroups""(""Id"") ON DELETE CASCADE,
            ""PersonnelId"" INTEGER NOT NULL REFERENCES ""Personnel""(""Id"") ON DELETE CASCADE,
            ""Role"" VARCHAR(100),
            ""StartDate"" DATE,
            ""EndDate"" DATE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_WorkGroupMembers_WorkGroupId_PersonnelId""
            ON ""WorkGroupMembers"" (""WorkGroupId"", ""PersonnelId"");

        -- AcademicStandingTypes table (วิทยฐานะ)
        CREATE TABLE IF NOT EXISTS ""AcademicStandingTypes"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""Code"" VARCHAR(50) NOT NULL,
            ""NameTh"" VARCHAR(200) NOT NULL,
            ""NameEn"" VARCHAR(200),
            ""Level"" INTEGER NOT NULL DEFAULT 0,
            ""IsActive"" BOOLEAN NOT NULL DEFAULT TRUE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AcademicStandingTypes_Code"" ON ""AcademicStandingTypes"" (""Code"");

        -- WorkGroups: add CreatedAt if missing
        ALTER TABLE ""WorkGroups"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW();

        -- ============================================================================
        -- Academic Calendar tables (from migration 20260406100956_AcademicCalendarStructure)
        -- These are created idempotently because the migration as a whole was baselined
        -- (it conflicted with pre-existing tables) but these specific tables were new.
        -- ============================================================================

        -- FiscalYears (ปีงบประมาณ)
        CREATE TABLE IF NOT EXISTS ""FiscalYears"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""Year"" INTEGER NOT NULL,
            ""NameTh"" VARCHAR(50) NOT NULL,
            ""StartDate"" DATE NOT NULL,
            ""EndDate"" DATE NOT NULL,
            ""IsActive"" BOOLEAN NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FiscalYears_Year"" ON ""FiscalYears"" (""Year"");

        -- Terms (ภาคเรียน)
        CREATE TABLE IF NOT EXISTS ""Terms"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""TermNumber"" INTEGER NOT NULL,
            ""NameTh"" VARCHAR(50) NOT NULL,
            ""NameEn"" VARCHAR(50) NOT NULL
        );

        -- GradeGroups (กลุ่มระดับชั้น)
        CREATE TABLE IF NOT EXISTS ""GradeGroups"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""Code"" VARCHAR(20) NOT NULL,
            ""NameTh"" VARCHAR(50) NOT NULL,
            ""SortOrder"" INTEGER NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_GradeGroups_Code"" ON ""GradeGroups"" (""Code"");

        -- AcademicYears (ปีการศึกษา) — references FiscalYears
        CREATE TABLE IF NOT EXISTS ""AcademicYears"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""Year"" INTEGER NOT NULL,
            ""NameTh"" VARCHAR(50) NOT NULL,
            ""FiscalYearStartId"" INTEGER NOT NULL,
            ""FiscalYearEndId"" INTEGER,
            ""StartDate"" DATE NOT NULL,
            ""EndDate"" DATE NOT NULL,
            ""IsActive"" BOOLEAN NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AcademicYears_Year"" ON ""AcademicYears"" (""Year"");
        CREATE INDEX IF NOT EXISTS ""IX_AcademicYears_FiscalYearStartId"" ON ""AcademicYears"" (""FiscalYearStartId"");
        CREATE INDEX IF NOT EXISTS ""IX_AcademicYears_FiscalYearEndId"" ON ""AcademicYears"" (""FiscalYearEndId"");
        DO $$ BEGIN
            ALTER TABLE ""AcademicYears""
                ADD CONSTRAINT ""FK_AcademicYears_FiscalYears_FiscalYearStartId""
                FOREIGN KEY (""FiscalYearStartId"") REFERENCES ""FiscalYears""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;
        DO $$ BEGIN
            ALTER TABLE ""AcademicYears""
                ADD CONSTRAINT ""FK_AcademicYears_FiscalYears_FiscalYearEndId""
                FOREIGN KEY (""FiscalYearEndId"") REFERENCES ""FiscalYears""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;

        -- Grades (ระดับชั้น) — references GradeGroups
        CREATE TABLE IF NOT EXISTS ""Grades"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""Code"" VARCHAR(20) NOT NULL,
            ""NameTh"" VARCHAR(50) NOT NULL,
            ""GradeGroupId"" INTEGER NOT NULL,
            ""SortOrder"" INTEGER NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Grades_Code"" ON ""Grades"" (""Code"");
        CREATE INDEX IF NOT EXISTS ""IX_Grades_GradeGroupId"" ON ""Grades"" (""GradeGroupId"");
        DO $$ BEGIN
            ALTER TABLE ""Grades""
                ADD CONSTRAINT ""FK_Grades_GradeGroups_GradeGroupId""
                FOREIGN KEY (""GradeGroupId"") REFERENCES ""GradeGroups""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;

        -- AcademicYearTerms (ปีการศึกษา x ภาคเรียน) — references AcademicYears + Terms
        CREATE TABLE IF NOT EXISTS ""AcademicYearTerms"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""AcademicYearId"" INTEGER NOT NULL,
            ""TermId"" INTEGER NOT NULL,
            ""StartDate"" DATE NOT NULL,
            ""EndDate"" DATE NOT NULL,
            ""IsActive"" BOOLEAN NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AcademicYearTerms_AcademicYearId_TermId""
            ON ""AcademicYearTerms"" (""AcademicYearId"", ""TermId"");
        CREATE INDEX IF NOT EXISTS ""IX_AcademicYearTerms_TermId"" ON ""AcademicYearTerms"" (""TermId"");
        DO $$ BEGIN
            ALTER TABLE ""AcademicYearTerms""
                ADD CONSTRAINT ""FK_AcademicYearTerms_AcademicYears_AcademicYearId""
                FOREIGN KEY (""AcademicYearId"") REFERENCES ""AcademicYears""(""Id"") ON DELETE CASCADE;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;
        DO $$ BEGIN
            ALTER TABLE ""AcademicYearTerms""
                ADD CONSTRAINT ""FK_AcademicYearTerms_Terms_TermId""
                FOREIGN KEY (""TermId"") REFERENCES ""Terms""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;

        -- ============================================================================
        -- Phase A.1 — Authority System foundation: Personnel + User extensions
        -- See docs/architecture/SBD-AUTHORITY-SYSTEM.md
        -- ============================================================================

        -- Personnel: academic & position metadata for /settings/personnel page
        ALTER TABLE ""Personnel"" ADD COLUMN IF NOT EXISTS ""PositionTypeId"" INTEGER NULL;
        ALTER TABLE ""Personnel"" ADD COLUMN IF NOT EXISTS ""AcademicStandingTypeId"" INTEGER NULL;
        ALTER TABLE ""Personnel"" ADD COLUMN IF NOT EXISTS ""SubjectArea"" VARCHAR(100) NULL;
        ALTER TABLE ""Personnel"" ADD COLUMN IF NOT EXISTS ""Specialty"" VARCHAR(200) NULL;
        ALTER TABLE ""Personnel"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" TIMESTAMPTZ NULL;
        ALTER TABLE ""Personnel"" ADD COLUMN IF NOT EXISTS ""UpdatedBy"" INTEGER NULL;
        CREATE INDEX IF NOT EXISTS ""IX_Personnel_SubjectArea"" ON ""Personnel"" (""SubjectArea"");
        DO $$ BEGIN
            ALTER TABLE ""Personnel""
                ADD CONSTRAINT ""FK_Personnel_PositionTypes_PositionTypeId""
                FOREIGN KEY (""PositionTypeId"") REFERENCES ""PositionTypes""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;
        DO $$ BEGIN
            ALTER TABLE ""Personnel""
                ADD CONSTRAINT ""FK_Personnel_AcademicStandingTypes_AcademicStandingTypeId""
                FOREIGN KEY (""AcademicStandingTypeId"") REFERENCES ""AcademicStandingTypes""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;

        -- User: cross-context personnel resolver (school|area|student|null)
        -- Named PersonnelContext (not PersonnelType) to avoid collision with the
        -- existing Personnel.PersonnelType column which has different semantics.
        ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""PersonnelContext"" VARCHAR(20) NULL;
        ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""PersonnelRefId"" INTEGER NULL;
        CREATE INDEX IF NOT EXISTS ""IX_Users_PersonnelContext_PersonnelRefId""
            ON ""Users"" (""PersonnelContext"", ""PersonnelRefId"")
            WHERE ""PersonnelContext"" IS NOT NULL;

        -- ============================================================================
        -- Phase A.1 — Area Personnel (PersonnelAdminApi bounded context)
        -- Tables prefixed `padm_` to isolate from core domain
        -- ============================================================================

        CREATE TABLE IF NOT EXISTS ""padm_area_personnel"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""UserId"" INTEGER NULL,
            ""AreaId"" INTEGER NOT NULL,
            ""PersonnelCode"" VARCHAR(50) NOT NULL,
            ""TitlePrefixId"" INTEGER NULL,
            ""FirstName"" VARCHAR(100) NOT NULL,
            ""LastName"" VARCHAR(100) NOT NULL,
            ""IdCard"" VARCHAR(13) NULL,
            ""PersonnelType"" VARCHAR(50) NOT NULL,
            ""PositionTypeId"" INTEGER NULL,
            ""AcademicStandingTypeId"" INTEGER NULL,
            ""WorkGroup"" VARCHAR(100) NULL,
            ""Gender"" CHAR(1) NOT NULL DEFAULT 'U',
            ""BirthDate"" DATE NULL,
            ""Phone"" VARCHAR(50) NULL,
            ""Email"" VARCHAR(200) NULL,
            ""LineId"" VARCHAR(100) NULL,
            ""Photo"" VARCHAR(1000) NULL,
            ""AddressId"" INTEGER NULL,
            ""StartDate"" DATE NULL,
            ""EndDate"" DATE NULL,
            ""IsActive"" BOOLEAN NOT NULL DEFAULT TRUE,
            ""CreatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""CreatedBy"" INTEGER NULL,
            ""UpdatedAt"" TIMESTAMPTZ NULL,
            ""UpdatedBy"" INTEGER NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""UX_padm_area_personnel_PersonnelCode""
            ON ""padm_area_personnel"" (""PersonnelCode"");
        CREATE UNIQUE INDEX IF NOT EXISTS ""UX_padm_area_personnel_UserId""
            ON ""padm_area_personnel"" (""UserId"") WHERE ""UserId"" IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ""IX_padm_area_personnel_IdCard""
            ON ""padm_area_personnel"" (""IdCard"");
        CREATE INDEX IF NOT EXISTS ""IX_padm_area_personnel_AreaId_IsActive""
            ON ""padm_area_personnel"" (""AreaId"", ""IsActive"");
        CREATE INDEX IF NOT EXISTS ""IX_padm_area_personnel_WorkGroup""
            ON ""padm_area_personnel"" (""WorkGroup"");
        DO $$ BEGIN
            ALTER TABLE ""padm_area_personnel""
                ADD CONSTRAINT ""FK_padm_area_personnel_Users_UserId""
                FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;
        DO $$ BEGIN
            ALTER TABLE ""padm_area_personnel""
                ADD CONSTRAINT ""FK_padm_area_personnel_Areas_AreaId""
                FOREIGN KEY (""AreaId"") REFERENCES ""Areas""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;
        DO $$ BEGIN
            ALTER TABLE ""padm_area_personnel""
                ADD CONSTRAINT ""FK_padm_area_personnel_PositionTypes_PositionTypeId""
                FOREIGN KEY (""PositionTypeId"") REFERENCES ""PositionTypes""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;
        DO $$ BEGIN
            ALTER TABLE ""padm_area_personnel""
                ADD CONSTRAINT ""FK_padm_area_personnel_AcademicStandingTypes_AcademicStandingTypeId""
                FOREIGN KEY (""AcademicStandingTypeId"") REFERENCES ""AcademicStandingTypes""(""Id"") ON DELETE RESTRICT;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;

        CREATE TABLE IF NOT EXISTS ""padm_area_personnel_educations"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""AreaPersonnelId"" INTEGER NOT NULL,
            ""Degree"" VARCHAR(200) NOT NULL,
            ""Major"" VARCHAR(200) NULL,
            ""Institution"" VARCHAR(300) NULL,
            ""GraduatedYear"" INTEGER NULL,
            ""CreatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""UpdatedAt"" TIMESTAMPTZ NULL
        );
        CREATE INDEX IF NOT EXISTS ""IX_padm_area_personnel_educations_AreaPersonnelId""
            ON ""padm_area_personnel_educations"" (""AreaPersonnelId"");
        DO $$ BEGIN
            ALTER TABLE ""padm_area_personnel_educations""
                ADD CONSTRAINT ""FK_padm_area_personnel_educations_padm_area_personnel""
                FOREIGN KEY (""AreaPersonnelId"") REFERENCES ""padm_area_personnel""(""Id"") ON DELETE CASCADE;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;

        CREATE TABLE IF NOT EXISTS ""padm_area_personnel_certifications"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""AreaPersonnelId"" INTEGER NOT NULL,
            ""Name"" VARCHAR(300) NOT NULL,
            ""Issuer"" VARCHAR(300) NULL,
            ""IssuedDate"" DATE NULL,
            ""ExpiryDate"" DATE NULL,
            ""CertificateNo"" VARCHAR(100) NULL,
            ""AttachmentPath"" VARCHAR(1000) NULL,
            ""CreatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""UpdatedAt"" TIMESTAMPTZ NULL
        );
        CREATE INDEX IF NOT EXISTS ""IX_padm_area_personnel_certifications_AreaPersonnelId""
            ON ""padm_area_personnel_certifications"" (""AreaPersonnelId"");
        DO $$ BEGIN
            ALTER TABLE ""padm_area_personnel_certifications""
                ADD CONSTRAINT ""FK_padm_area_personnel_certifications_padm_area_personnel""
                FOREIGN KEY (""AreaPersonnelId"") REFERENCES ""padm_area_personnel""(""Id"") ON DELETE CASCADE;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;
    ");
    Console.WriteLine("[Migration] Gateway shadow properties + academic calendar + Authority A.1 tables ensured.");

    if (!await db.Modules.AnyAsync())
    {
        var now = DateTimeOffset.UtcNow;
        var modules = new[]
        {
            new SBD.Domain.Entities.Module { Code = "core-dashboard",  Name = "Dashboard",         Category = "Core",    Icon = "📊", SortOrder = 1,  IsDefault = true,  IsEnabled = true, Description = "แดชบอร์ดหลักของระบบ" },
            new SBD.Domain.Entities.Module { Code = "user-mgmt",       Name = "จัดการผู้ใช้",        Category = "Core",    Icon = "👥", SortOrder = 2,  IsDefault = true,  IsEnabled = true, Description = "จัดการผู้ใช้งานระบบทั้งหมด" },
            new SBD.Domain.Entities.Module { Code = "school-mgmt",     Name = "จัดการโรงเรียน",      Category = "Core",    Icon = "🏫", SortOrder = 3,  IsDefault = true,  IsEnabled = true, Description = "จัดการข้อมูลโรงเรียนในระบบ" },
            new SBD.Domain.Entities.Module { Code = "area-mgmt",       Name = "จัดการเขตพื้นที่",     Category = "Core",    Icon = "🗺️", SortOrder = 4, IsDefault = true,  IsEnabled = true, Description = "จัดการเขตพื้นที่การศึกษา" },
            new SBD.Domain.Entities.Module { Code = "curriculum",      Name = "หลักสูตรสถานศึกษา",  Category = "Feature", Icon = "📚", SortOrder = 10, IsDefault = false, IsEnabled = true, RoutePath = "curriculum", Description = "ระบบเขียนหลักสูตรสถานศึกษา", AssignableToTeacher = true },
            new SBD.Domain.Entities.Module { Code = "attendance",      Name = "ระบบเช็คชื่อ",       Category = "Feature", Icon = "✅", SortOrder = 11, IsDefault = false, IsEnabled = true, RoutePath = "attendance", Description = "บันทึกการเข้าเรียนรายวัน", AssignableToTeacher = true, AssignableToStudent = true },
            new SBD.Domain.Entities.Module { Code = "gradebook",       Name = "สมุดเกรด",           Category = "Feature", Icon = "📝", SortOrder = 12, IsDefault = false, IsEnabled = true, RoutePath = "gradebook",  Description = "บันทึกและคำนวณผลการเรียน", AssignableToTeacher = true, AssignableToStudent = true },
            new SBD.Domain.Entities.Module { Code = "aplan",          Name = "ระบบแผนปฏิบัติการและงบประมาณประจำปี", Category = "Feature", Icon = "📋", SortOrder = 13, IsDefault = false, IsEnabled = true, RoutePath = "aplan", Description = "บริหารแผนปฏิบัติการประจำปีและงบประมาณ", AssignableToTeacher = false, AssignableToStudent = true },
        };

        // Shadow property visibility/registration seed values
        var visibilityMap = new Dictionary<string, string>
        {
            ["core-dashboard"] = "public,student,teacher,school,area,superadmin",
            ["user-mgmt"] = "superadmin,area,school",
            ["school-mgmt"] = "superadmin,area,school",
            ["area-mgmt"] = "superadmin,area",
            ["curriculum"] = "teacher,school,area",
            ["attendance"] = "student,teacher,school,area",
            ["gradebook"] = "student,teacher,school,area",
            ["aplan"] = "student,teacher,school,area",
        };

        db.Modules.AddRange(modules);

        // Set shadow properties via ChangeTracker
        foreach (var module in modules)
        {
            var entry = db.Entry(module);
            entry.Property("VisibilityLevels").CurrentValue = visibilityMap[module.Code];
            entry.Property("RegistrationType").CurrentValue = "internal";
            entry.Property("CreatedAt").CurrentValue = now;
            entry.Property("UpdatedAt").CurrentValue = now;
        }

        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] Default modules created.");
    }

    // Ensure new feature modules exist (idempotent — runs on every startup)
    var ensureModules = new (string Code, string Name, string Icon, int SortOrder, string RoutePath, string Description, bool AssignableToTeacher, bool AssignableToStudent, string Visibility)[]
    {
        ("ssms",  "ระบบนิเทศการศึกษา",                           "📋", 14, "ssms",  "นิเทศ ติดตาม และประเมินผลการศึกษา",            false, false, "teacher,school,area"),
        ("sss",   "ระบบดูแลช่วยเหลือนักเรียน",                    "🤝", 15, "sss",   "ระบบดูแลช่วยเหลือนักเรียน",                    true,  true,  "student,teacher,school,area"),
        ("reqd",  "ระบบวิจัยเพื่อพัฒนาคุณภาพการศึกษา",            "🔬", 16, "reqd",  "การวิจัยเพื่อพัฒนาคุณภาพการศึกษา",             true,  false, "teacher,school,area"),
        ("aplan", "ระบบแผนปฏิบัติการและงบประมาณประจำปี",           "📋", 17, "aplan", "บริหารแผนปฏิบัติการประจำปีและงบประมาณ",         false, true,  "student,teacher,school,area"),
        ("edm",   "ระบบงานสารบรรณอิเล็กทรอนิกส์",               "📝", 18, "edm",   "ระบบจัดการเอกสารราชการอิเล็กทรอนิกส์",          true,  false, "teacher,school,area"),
        ("psnl",  "ระบบบริหารงานบุคคล",                         "👔", 19, "psnl",  "ระบบบริหารงานบุคคลของเขตพื้นที่และโรงเรียน",    true,  true,  "student,teacher,school,area"),
    };

    foreach (var m in ensureModules)
    {
        if (!await db.Modules.AnyAsync(x => x.Code == m.Code))
        {
            var newModule = new SBD.Domain.Entities.Module
            {
                Code = m.Code, Name = m.Name, Category = "Feature", Icon = m.Icon,
                SortOrder = m.SortOrder, IsDefault = false, IsEnabled = true,
                RoutePath = m.RoutePath, Description = m.Description,
                AssignableToTeacher = m.AssignableToTeacher, AssignableToStudent = m.AssignableToStudent,
                VisibilityLevels = m.Visibility, RegistrationType = "internal",
            };
            db.Modules.Add(newModule);
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] Module '{m.Code}' added.");
        }
    }

    // Seed schools from สพป.ศรีสะเกษ เขต 3
    await Gateway.SchoolSeedData.SeedAsync(db);

    // Seed academic calendar structure
    await Gateway.AcademicCalendarSeedData.SeedAsync(db);

    // ── Phase A.2.5 — Seed AreaPermissionPolicy default rows for self-edit ──
    // For every Area, ensure the 4 self-edit policy codes exist (default = false).
    // Admins toggle these from /administrator/areas/{id}/policies. The
    // PersonnelMeController + AreaPersonnelMeController in PersonnelApi /
    // PersonnelAdminApi consult these flags before allowing field-level edits.
    var selfEditPolicyCodes = new (string Code, string Description)[]
    {
        ("personnel.self_edit_subject",
         "อนุญาตให้ครูแก้ไขกลุ่มสาระ/วิชาเอกของตนเอง"),
        ("personnel.self_edit_birthdate",
         "อนุญาตให้บุคลากรแก้ไขวันเกิดของตนเอง"),
        ("personnel.self_edit_education",
         "อนุญาตให้บุคลากรเพิ่ม/แก้ไข/ลบประวัติการศึกษาของตนเอง"),
    };
    var areas = await db.Areas.AsNoTracking().Select(a => a.Id).ToListAsync();
    foreach (var areaId in areas)
    {
        foreach (var (code, desc) in selfEditPolicyCodes)
        {
            var exists = await db.AreaPermissionPolicies
                .AnyAsync(p => p.AreaId == areaId && p.PermissionCode == code);
            if (!exists)
            {
                db.AreaPermissionPolicies.Add(new SBD.Domain.Entities.AreaPermissionPolicy
                {
                    AreaId = areaId,
                    PermissionCode = code,
                    AllowSchoolAdmin = false, // off by default — admin opts in
                    Description = desc,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
        }
    }
    if (db.ChangeTracker.HasChanges())
    {
        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] AreaPermissionPolicy self-edit rows ensured.");
    }
}

// Configure the HTTP request pipeline
var enableSwagger = app.Environment.IsDevelopment()
    || string.Equals(app.Configuration["ENABLE_SWAGGER"], "true", StringComparison.OrdinalIgnoreCase);
if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SBD Gateway API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health check / redirect root to swagger
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();
