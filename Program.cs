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
    options.UseNpgsql(connectionString, npgsql =>
               npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null))
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
           .ConfigureWarnings(w => w.Ignore(
               Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddScoped<SbdDbContext, GatewayDbContext>();

// Phase B.4: capability grant lookup with Redis cache (cap_v keyed)
builder.Services.AddScoped<Gateway.Services.ICapabilityService, Gateway.Services.CapabilityService>();

// Add MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    // Consumers
    x.AddConsumer<Gateway.Consumers.SchoolLogoUpdatedConsumer>();
    x.AddConsumer<Gateway.Consumers.CacheInvalidateConsumer>();

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

// Add Controllers — camelCase JSON so all API responses match Angular expectations
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNamingPolicy        = System.Text.Json.JsonNamingPolicy.CamelCase;
        opt.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

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
        catch (PostgresException ex) when (ex.SqlState is "42P07" or "42701")
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

    // ── Phase B.1 — Authority System: authz_* tables ─────────────────────────
    await db.Database.ExecuteSqlRawAsync(@"
        -- CapabilityDefinition: catalog of all system + module capabilities
        CREATE TABLE IF NOT EXISTS ""authz_capability_definitions"" (
            ""Id""                  SERIAL PRIMARY KEY,
            ""Code""                VARCHAR(200) NOT NULL,
            ""Module""              VARCHAR(100) NOT NULL,
            ""Resource""            VARCHAR(100) NOT NULL,
            ""Action""              VARCHAR(100) NOT NULL,
            ""NameTh""              VARCHAR(300) NOT NULL,
            ""Description""         VARCHAR(1000),
            ""DefaultScope""        VARCHAR(50)  NOT NULL,
            ""IsRedelegatable""     BOOLEAN NOT NULL DEFAULT TRUE,
            ""MaxDelegationDepth""  INTEGER NOT NULL DEFAULT 5,
            ""IsDangerous""         BOOLEAN NOT NULL DEFAULT FALSE,
            ""RequiresApproval""    BOOLEAN NOT NULL DEFAULT FALSE,
            ""IsActive""            BOOLEAN NOT NULL DEFAULT TRUE,
            ""CreatedAt""           TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""UX_authz_cap_def_code""
            ON ""authz_capability_definitions"" (""Code"");

        -- CapabilityGrant: individual grant records with delegation chain
        CREATE TABLE IF NOT EXISTS ""authz_capability_grants"" (
            ""Id""                  BIGSERIAL PRIMARY KEY,
            ""GranteeUserId""       INTEGER NOT NULL,
            ""CapabilityCode""      VARCHAR(200) NOT NULL,
            ""ScopeType""           VARCHAR(50)  NOT NULL,
            ""ScopeId""             INTEGER,
            ""GrantedByUserId""     INTEGER NOT NULL,
            ""ParentGrantId""       BIGINT REFERENCES ""authz_capability_grants""(""Id"") ON DELETE RESTRICT,
            ""RedelegationDepth""   INTEGER NOT NULL DEFAULT 0,
            ""CanRedelegate""       BOOLEAN NOT NULL DEFAULT FALSE,
            ""RemainingDepth""      INTEGER NOT NULL DEFAULT 0,
            ""ExpiresAt""           DATE,
            ""ConditionsJson""      JSONB,
            ""GrantedAt""           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""GrantReason""         VARCHAR(500),
            ""OrderRef""            VARCHAR(100),
            ""RevokedAt""           TIMESTAMPTZ,
            ""RevokedByUserId""     INTEGER,
            ""RevokeReason""        VARCHAR(500)
        );
        CREATE INDEX IF NOT EXISTS ""IX_authz_grants_grantee_cap_scope""
            ON ""authz_capability_grants"" (""GranteeUserId"", ""CapabilityCode"", ""ScopeType"", ""ScopeId"");
        CREATE INDEX IF NOT EXISTS ""IX_authz_grants_parent""
            ON ""authz_capability_grants"" (""ParentGrantId"") WHERE ""ParentGrantId"" IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ""IX_authz_grants_expires""
            ON ""authz_capability_grants"" (""ExpiresAt"") WHERE ""ExpiresAt"" IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ""IX_authz_grants_active""
            ON ""authz_capability_grants"" (""GranteeUserId"") WHERE ""RevokedAt"" IS NULL;

        -- FunctionalRoleType: catalog of functional roles (หน้าที่)
        CREATE TABLE IF NOT EXISTS ""authz_functional_role_types"" (
            ""Id""                       SERIAL PRIMARY KEY,
            ""Code""                     VARCHAR(100) NOT NULL,
            ""NameTh""                   VARCHAR(200) NOT NULL,
            ""Category""                 VARCHAR(50)  NOT NULL,
            ""ContextScope""             VARCHAR(50)  NOT NULL,
            ""GrantedCapabilitiesJson""  JSONB NOT NULL DEFAULT '[]'::jsonb,
            ""CanBeAssignedByJson""      JSONB,
            ""IsSystem""                 BOOLEAN NOT NULL DEFAULT FALSE,
            ""IsActive""                 BOOLEAN NOT NULL DEFAULT TRUE,
            ""CreatedAt""                TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""UX_authz_func_role_code""
            ON ""authz_functional_role_types"" (""Code"");

        -- FunctionalAssignment: user → functional role at a context scope
        CREATE TABLE IF NOT EXISTS ""authz_functional_assignments"" (
            ""Id""                   BIGSERIAL PRIMARY KEY,
            ""UserId""               INTEGER NOT NULL,
            ""FunctionalRoleTypeId"" INTEGER NOT NULL REFERENCES ""authz_functional_role_types""(""Id"") ON DELETE RESTRICT,
            ""ContextScopeType""     VARCHAR(50) NOT NULL,
            ""ContextScopeId""       INTEGER NOT NULL,
            ""StartDate""            DATE NOT NULL,
            ""EndDate""              DATE,
            ""AssignedByUserId""     INTEGER NOT NULL,
            ""AssignedAt""           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""OrderRef""             VARCHAR(100),
            ""RevokedAt""            TIMESTAMPTZ,
            ""RevokedByUserId""      INTEGER
        );
        CREATE INDEX IF NOT EXISTS ""IX_authz_func_assign_user_role_scope""
            ON ""authz_functional_assignments"" (""UserId"", ""FunctionalRoleTypeId"", ""ContextScopeType"", ""ContextScopeId"");
        CREATE INDEX IF NOT EXISTS ""IX_authz_func_assign_enddate""
            ON ""authz_functional_assignments"" (""EndDate"") WHERE ""EndDate"" IS NOT NULL;

        -- GrantAuditLog: immutable append-only audit trail with hash chain
        CREATE TABLE IF NOT EXISTS ""authz_grant_audit_logs"" (
            ""Id""              BIGSERIAL PRIMARY KEY,
            ""OccurredAt""      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""ActorUserId""     INTEGER NOT NULL,
            ""Action""          VARCHAR(50) NOT NULL,
            ""TargetUserId""    INTEGER,
            ""CapabilityCode""  VARCHAR(200),
            ""ScopeType""       VARCHAR(50),
            ""ScopeId""         INTEGER,
            ""GrantId""         BIGINT,
            ""Reason""          VARCHAR(500),
            ""IpAddress""       VARCHAR(50),
            ""MetadataJson""    JSONB,
            ""PrevLogHash""     VARCHAR(64),
            ""ThisLogHash""     VARCHAR(64) NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ""IX_authz_audit_occurred""
            ON ""authz_grant_audit_logs"" (""OccurredAt"");
        CREATE INDEX IF NOT EXISTS ""IX_authz_audit_actor""
            ON ""authz_grant_audit_logs"" (""ActorUserId"");
        CREATE INDEX IF NOT EXISTS ""IX_authz_audit_grant""
            ON ""authz_grant_audit_logs"" (""GrantId"") WHERE ""GrantId"" IS NOT NULL;

        -- Prevent UPDATE and DELETE on audit log (tamper-evident)
        DO $$ BEGIN
            CREATE RULE ""authz_audit_no_update"" AS ON UPDATE TO ""authz_grant_audit_logs"" DO INSTEAD NOTHING;
            CREATE RULE ""authz_audit_no_delete"" AS ON DELETE TO ""authz_grant_audit_logs"" DO INSTEAD NOTHING;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;

        -- GrantApprovalRequest: approval workflow for dangerous capabilities
        CREATE TABLE IF NOT EXISTS ""authz_grant_approval_requests"" (
            ""Id""                       BIGSERIAL PRIMARY KEY,
            ""RequesterUserId""          INTEGER NOT NULL,
            ""TargetUserId""             INTEGER NOT NULL,
            ""CapabilityDefinitionId""   INTEGER NOT NULL REFERENCES ""authz_capability_definitions""(""Id"") ON DELETE RESTRICT,
            ""ScopeType""                VARCHAR(50) NOT NULL,
            ""ScopeId""                  INTEGER,
            ""Reason""                   VARCHAR(1000),
            ""RequestedAt""              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""Status""                   VARCHAR(20) NOT NULL DEFAULT 'pending',
            ""ApprovedByUserId""         INTEGER,
            ""ApprovedAt""               TIMESTAMPTZ,
            ""ApprovalNote""             VARCHAR(1000)
        );
        CREATE INDEX IF NOT EXISTS ""IX_authz_approval_status_requester""
            ON ""authz_grant_approval_requests"" (""Status"", ""RequesterUserId"");
    ");
    Console.WriteLine("[Migration] Authority B.1 tables (authz_*) ensured.");

    // ── Phase D — Enterprise Hardening: authz_jit_*, authz_recertification_*, user_risk_scores ─
    await db.Database.ExecuteSqlRawAsync(@"
        -- D.2: JIT Elevations — temporary capability grants (max 8h TTL)
        CREATE TABLE IF NOT EXISTS ""authz_jit_elevations"" (
            ""Id""                  BIGSERIAL PRIMARY KEY,
            ""UserId""              INTEGER NOT NULL REFERENCES ""Users""(""Id"") ON DELETE CASCADE,
            ""CapabilityCode""      VARCHAR(200) NOT NULL,
            ""ScopeType""           VARCHAR(50)  NOT NULL,
            ""ScopeId""             INTEGER,
            ""GrantedByUserId""     INTEGER NOT NULL,
            ""Reason""              VARCHAR(1000) NOT NULL,
            ""GrantedAt""           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""ExpiresAt""           TIMESTAMPTZ NOT NULL,
            ""RevokedAt""           TIMESTAMPTZ,
            ""RevokedByUserId""     INTEGER,
            ""RevokeReason""        VARCHAR(200)
        );
        CREATE INDEX IF NOT EXISTS ""IX_authz_jit_user_active""
            ON ""authz_jit_elevations"" (""UserId"", ""CapabilityCode"")
            WHERE ""RevokedAt"" IS NULL;
        CREATE INDEX IF NOT EXISTS ""IX_authz_jit_expires""
            ON ""authz_jit_elevations"" (""ExpiresAt"")
            WHERE ""RevokedAt"" IS NULL;

        -- D.4: Recertification campaigns
        CREATE TABLE IF NOT EXISTS ""authz_recertification_campaigns"" (
            ""Id""          BIGSERIAL PRIMARY KEY,
            ""Name""        VARCHAR(300) NOT NULL,
            ""StartedAt""   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""StartedBy""   INTEGER NOT NULL,
            ""Deadline""    DATE NOT NULL,
            ""Status""      VARCHAR(20) NOT NULL DEFAULT 'active',
            ""CompletedAt"" TIMESTAMPTZ
        );
        CREATE INDEX IF NOT EXISTS ""IX_authz_recert_campaigns_status""
            ON ""authz_recertification_campaigns"" (""Status"");

        -- D.4: Recertification items (one per grant in campaign)
        CREATE TABLE IF NOT EXISTS ""authz_recertification_items"" (
            ""Id""              BIGSERIAL PRIMARY KEY,
            ""CampaignId""      BIGINT NOT NULL REFERENCES ""authz_recertification_campaigns""(""Id"") ON DELETE CASCADE,
            ""GrantId""         BIGINT NOT NULL REFERENCES ""authz_capability_grants""(""Id"") ON DELETE CASCADE,
            ""GranteeUserId""   INTEGER NOT NULL,
            ""ReviewerUserId""  INTEGER,
            ""Status""          VARCHAR(20) NOT NULL DEFAULT 'pending',
            ""ReviewedAt""      TIMESTAMPTZ,
            ""ReviewNote""      VARCHAR(1000)
        );
        CREATE INDEX IF NOT EXISTS ""IX_authz_recert_items_campaign_status""
            ON ""authz_recertification_items"" (""CampaignId"", ""Status"");
        CREATE INDEX IF NOT EXISTS ""IX_authz_recert_items_grant""
            ON ""authz_recertification_items"" (""GrantId"");
        CREATE UNIQUE INDEX IF NOT EXISTS ""UX_authz_recert_items_campaign_grant""
            ON ""authz_recertification_items"" (""CampaignId"", ""GrantId"");

        -- D.6: User risk scores — refreshed periodically by WorkerService
        CREATE TABLE IF NOT EXISTS ""user_risk_scores"" (
            ""UserId""          INTEGER PRIMARY KEY REFERENCES ""Users""(""Id"") ON DELETE CASCADE,
            ""Score""           INTEGER NOT NULL DEFAULT 0,
            ""Level""           VARCHAR(20) NOT NULL DEFAULT 'low',
            ""FactorsJson""     JSONB NOT NULL DEFAULT '[]'::jsonb,
            ""LastScoredAt""    TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS ""IX_user_risk_scores_level""
            ON ""user_risk_scores"" (""Level"");
    ");
    Console.WriteLine("[Migration] Phase D enterprise tables (authz_jit_*, authz_recertification_*, user_risk_scores) ensured.");

    // ── Phase D.1 — StudentApi bounded context tables ────────────────────────
    // StudentProfile + sub-tables. StudentApi reads these directly via SbdDbContext.
    await db.Database.ExecuteSqlRawAsync(@"
        -- StudentProfile (main record, linked to Personnel + User)
        -- CREATE TABLE handles fresh installs; ALTER TABLE handles partial/EF-created tables
        CREATE TABLE IF NOT EXISTS ""StudentProfiles"" (
            ""Id"" SERIAL PRIMARY KEY
        );
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""PersonnelId""        INTEGER REFERENCES ""Personnel""(""Id"") ON DELETE RESTRICT;
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""StudentCode""         VARCHAR(50);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""Status""              VARCHAR(30) NOT NULL DEFAULT 'active';
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""SchoolId""            INTEGER REFERENCES ""Schools""(""Id"") ON DELETE RESTRICT;
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""EnrollYear""          INTEGER;
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""GraduateYear""        INTEGER;
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""GradeLevel""          VARCHAR(20);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""Classroom""           VARCHAR(50);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""AdvisorId""           INTEGER REFERENCES ""Personnel""(""Id"") ON DELETE SET NULL;
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""Nationality""         VARCHAR(100);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""Religion""            VARCHAR(100);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""DisabilityType""      VARCHAR(100);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""IsDisadvantaged""     BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""DistanceToSchoolKm""  NUMERIC(6,1);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""ParentName""          VARCHAR(200);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""ParentPhone""         VARCHAR(50);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""ParentRelation""      VARCHAR(100);
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""CreatedAt""           TIMESTAMPTZ NOT NULL DEFAULT NOW();
        ALTER TABLE ""StudentProfiles"" ADD COLUMN IF NOT EXISTS ""UpdatedAt""           TIMESTAMPTZ;
        CREATE UNIQUE INDEX IF NOT EXISTS ""UX_StudentProfiles_StudentCode"" ON ""StudentProfiles"" (""StudentCode"") WHERE ""StudentCode"" IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ""UX_StudentProfiles_PersonnelId"" ON ""StudentProfiles"" (""PersonnelId"") WHERE ""PersonnelId"" IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ""IX_StudentProfiles_SchoolId"" ON ""StudentProfiles"" (""SchoolId"");
        CREATE INDEX IF NOT EXISTS ""IX_StudentProfiles_Status"" ON ""StudentProfiles"" (""Status"");

        -- StudentAcademics (academic records per year+semester)
        CREATE TABLE IF NOT EXISTS ""StudentAcademics"" (
            ""Id"" SERIAL PRIMARY KEY
        );
        ALTER TABLE ""StudentAcademics"" ADD COLUMN IF NOT EXISTS ""StudentProfileId"" INTEGER REFERENCES ""StudentProfiles""(""Id"") ON DELETE CASCADE;
        ALTER TABLE ""StudentAcademics"" ADD COLUMN IF NOT EXISTS ""AcademicYear""  INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE ""StudentAcademics"" ADD COLUMN IF NOT EXISTS ""Semester""      INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE ""StudentAcademics"" ADD COLUMN IF NOT EXISTS ""Gpa""           NUMERIC(4,2);
        ALTER TABLE ""StudentAcademics"" ADD COLUMN IF NOT EXISTS ""SubjectGrades"" JSONB NOT NULL DEFAULT '{{}}'::jsonb;
        CREATE UNIQUE INDEX IF NOT EXISTS ""UX_StudentAcademics_Profile_Year_Sem""
            ON ""StudentAcademics"" (""StudentProfileId"", ""AcademicYear"", ""Semester"") WHERE ""StudentProfileId"" IS NOT NULL;

        -- StudentActivities (extra-curricular / honors)
        CREATE TABLE IF NOT EXISTS ""StudentActivities"" (
            ""Id"" SERIAL PRIMARY KEY
        );
        ALTER TABLE ""StudentActivities"" ADD COLUMN IF NOT EXISTS ""StudentProfileId"" INTEGER REFERENCES ""StudentProfiles""(""Id"") ON DELETE CASCADE;
        ALTER TABLE ""StudentActivities"" ADD COLUMN IF NOT EXISTS ""Type""           VARCHAR(50) NOT NULL DEFAULT '';
        ALTER TABLE ""StudentActivities"" ADD COLUMN IF NOT EXISTS ""Title""          VARCHAR(500) NOT NULL DEFAULT '';
        ALTER TABLE ""StudentActivities"" ADD COLUMN IF NOT EXISTS ""ActivityDate""   DATE;
        ALTER TABLE ""StudentActivities"" ADD COLUMN IF NOT EXISTS ""AttachmentPath"" VARCHAR(1000);
        CREATE INDEX IF NOT EXISTS ""IX_StudentActivities_ProfileId"" ON ""StudentActivities"" (""StudentProfileId"");

        -- StudentHealthRecords (physical health per term)
        CREATE TABLE IF NOT EXISTS ""StudentHealthRecords"" (
            ""Id"" SERIAL PRIMARY KEY
        );
        ALTER TABLE ""StudentHealthRecords"" ADD COLUMN IF NOT EXISTS ""StudentProfileId"" INTEGER REFERENCES ""StudentProfiles""(""Id"") ON DELETE CASCADE;
        ALTER TABLE ""StudentHealthRecords"" ADD COLUMN IF NOT EXISTS ""AcademicYear""   INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE ""StudentHealthRecords"" ADD COLUMN IF NOT EXISTS ""Term""           INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE ""StudentHealthRecords"" ADD COLUMN IF NOT EXISTS ""RecordDate""     DATE;
        ALTER TABLE ""StudentHealthRecords"" ADD COLUMN IF NOT EXISTS ""WeightKg""       NUMERIC(5,1);
        ALTER TABLE ""StudentHealthRecords"" ADD COLUMN IF NOT EXISTS ""HeightCm""       NUMERIC(5,1);
        ALTER TABLE ""StudentHealthRecords"" ADD COLUMN IF NOT EXISTS ""Bmi""            NUMERIC(4,1);
        ALTER TABLE ""StudentHealthRecords"" ADD COLUMN IF NOT EXISTS ""HealthStatus""   VARCHAR(100);
        CREATE INDEX IF NOT EXISTS ""IX_StudentHealthRecords_ProfileId"" ON ""StudentHealthRecords"" (""StudentProfileId"");
    ");
    Console.WriteLine("[Migration] Phase D.1 StudentProfile tables ensured.");

    // ── CacheDefinitions — Redis key registry (managed by SuperAdmin) ─────────
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""CacheDefinitions"" (
            ""Id""                  SERIAL PRIMARY KEY,
            ""CacheKeyPattern""     VARCHAR(200) NOT NULL,
            ""Name""                VARCHAR(100) NOT NULL,
            ""Description""         TEXT,
            ""GroupPrefix""         VARCHAR(100) NOT NULL,
            ""DbIndex""             INTEGER NOT NULL DEFAULT 0,
            ""SuggestedTtlMinutes"" INTEGER,
            ""IsActive""            BOOLEAN NOT NULL DEFAULT TRUE,
            ""CreatedAt""           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""UpdatedAt""           TIMESTAMPTZ
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CacheDefinitions_Pattern_Db""
            ON ""CacheDefinitions"" (""CacheKeyPattern"", ""DbIndex"");
    ");

    // Seed default key definitions (idempotent — skip if any exist)
    var hasDefs = await db.Database.ExecuteSqlRawAsync(
        @"INSERT INTO ""CacheDefinitions"" (""CacheKeyPattern"",""Name"",""Description"",""GroupPrefix"",""DbIndex"",""SuggestedTtlMinutes"",""IsActive"",""CreatedAt"")
          SELECT p,n,d,g,db,ttl,true,NOW() FROM (VALUES
            ('refdata:',           'Reference Data',          'ข้อมูลอ้างอิง (จังหวัด อำเภอ บทบาท โมดูล)',            'refdata:', 0, 1440),
            ('svc:',               'Service Registry',        'รายการ microservice ที่ลงทะเบียนใน Redis',              'svc:', 0, NULL),
            ('rate:',              'Rate Limiting',           'การจำกัด API call ต่อ IP/user',                         'rate:', 0, 1),
            ('cache:',             'General API Cache',       'Response cache อื่นๆ จาก ICacheService',                'cache:', 0, 60),
            ('refresh_token:',     'Refresh Token Hash',      'SHA-256 hash ของ refresh token แต่ละ session (90 วัน)', 'refresh_token:', 1, 129600),
            ('user_sessions:',     'User Sessions Set',       'SET ของ sessionId ทั้งหมดของแต่ละ user',               'user_sessions:', 1, 129600),
            ('session_meta:',      'Session Metadata',        'ข้อมูล device, IP, User-Agent, LastSeenAt ของ session', 'session_meta:', 1, 129600),
            ('session_token:',     'Session Token Reference', 'อ้างอิง token hash ปัจจุบันของ session (ใช้ rotate)',   'session_token:', 1, 129600)
          ) AS t(p,n,d,g,db,ttl)
          ON CONFLICT (""CacheKeyPattern"", ""DbIndex"") DO NOTHING");
    Console.WriteLine("[Seed] CacheDefinitions table ensured.");

    // ── MenuItems — DB-backed sidebar menu per role (incl. public Guest menu) ──
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""MenuItems"" (
            ""Id""            SERIAL PRIMARY KEY,
            ""Role""          VARCHAR(50)  NOT NULL,
            ""ItemKey""       VARCHAR(100) NOT NULL,
            ""Label""         VARCHAR(200) NOT NULL,
            ""Icon""          VARCHAR(200) NOT NULL,
            ""RouteTemplate"" VARCHAR(500) NOT NULL,
            ""SortOrder""     INT          NOT NULL DEFAULT 0,
            ""IsActive""      BOOLEAN      NOT NULL DEFAULT TRUE,
            ""ExactMatch""    BOOLEAN      NOT NULL DEFAULT FALSE,
            ""Badge""         VARCHAR(50),
            ""Gradient""      VARCHAR(200),
            ""CreatedAt""     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            ""UpdatedAt""     TIMESTAMPTZ,
            ""UpdatedBy""     INT,
            CONSTRAINT ""UQ_MenuItems_Role_Key"" UNIQUE (""Role"", ""ItemKey"")
        );
        CREATE INDEX IF NOT EXISTS ""IX_MenuItems_Role_Active""
            ON ""MenuItems"" (""Role"", ""IsActive"");
        ALTER TABLE ""MenuItems"" ADD COLUMN IF NOT EXISTS ""Gradient"" VARCHAR(200);
    ");

    await db.Database.ExecuteSqlRawAsync(@"
        INSERT INTO ""MenuItems"" (""Role"",""ItemKey"",""Label"",""Icon"",""RouteTemplate"",""SortOrder"",""IsActive"",""ExactMatch"",""Gradient"") VALUES
        ('SuperAdmin','dashboard','ภาพรวมระบบ','fas fa-chart-pie','/administrator',1,TRUE,TRUE,NULL),
        ('SuperAdmin','users','จัดการผู้ใช้','fas fa-users','/administrator/users',2,TRUE,FALSE,NULL),
        ('SuperAdmin','schools','จัดการโรงเรียน','fas fa-school','/administrator/schools',3,TRUE,FALSE,NULL),
        ('SuperAdmin','areas','จัดการเขตพื้นที่','fas fa-map-marked-alt','/administrator/areas',4,TRUE,FALSE,NULL),
        ('SuperAdmin','modules','จัดการโมดูล','fas fa-puzzle-piece','/administrator/modules',5,TRUE,FALSE,NULL),
        ('SuperAdmin','positions','ตำแหน่ง','fas fa-id-badge','/administrator/positions',6,TRUE,FALSE,NULL),
        ('SuperAdmin','notifications','การแจ้งเตือน','fas fa-bell','/administrator/notifications',7,TRUE,FALSE,NULL),
        ('SuperAdmin','api-status','สถานะ API','fas fa-heartbeat','/administrator/api-status',8,TRUE,FALSE,NULL),
        ('SuperAdmin','minio','ที่เก็บไฟล์','fas fa-database','/administrator/minio',9,TRUE,FALSE,NULL),
        ('SuperAdmin','apikeys','API Keys','fas fa-key','/administrator/apikeys',10,TRUE,FALSE,NULL),
        ('SuperAdmin','redis','Redis','fas fa-bolt','/administrator/redis',11,TRUE,FALSE,NULL),
        ('SuperAdmin','rabbitmq','คิวงาน','fas fa-exchange-alt','/administrator/rabbitmq',12,TRUE,FALSE,NULL),
        ('SuperAdmin','signalr','SignalR','fas fa-satellite-dish','/administrator/signalr',13,TRUE,FALSE,NULL),
        ('SuperAdmin','mcp','MCP Tools','fas fa-robot','/administrator/mcp',14,TRUE,FALSE,NULL),
        ('SuperAdmin','menu-management','จัดการเมนู','fas fa-list-ul','/administrator/menu-management',15,TRUE,FALSE,NULL),
        ('AreaAdmin','dashboard','ภาพรวมเขต','fas fa-chart-pie','{basePath}',1,TRUE,TRUE,NULL),
        ('AreaAdmin','schools','โรงเรียน','fas fa-school','{basePath}/schools',2,TRUE,FALSE,NULL),
        ('AreaAdmin','students','นักเรียน','fas fa-user-graduate','{basePath}/students',3,TRUE,FALSE,NULL),
        ('AreaAdmin','teachers','ครู','fas fa-chalkboard-teacher','{basePath}/teachers',4,TRUE,FALSE,NULL),
        ('AreaAdmin','personnel','บุคลากร','fas fa-users','{basePath}/personnel',5,TRUE,FALSE,NULL),
        ('AreaAdmin','users','จัดการผู้ใช้','fas fa-users-cog','{basePath}/users',6,TRUE,FALSE,NULL),
        ('AreaAdmin','delegation','มอบหมายงาน','fas fa-id-badge','{basePath}/personnel/delegation',7,TRUE,FALSE,NULL),
        ('AreaAdmin','policies','สิทธิ์การใช้งาน','fas fa-sliders','{basePath}/policies',8,TRUE,FALSE,NULL),
        ('AreaAdmin','academics','วิชาการ','fas fa-graduation-cap','{basePath}/academics',9,TRUE,FALSE,NULL),
        ('AreaAdmin','modules','โมดูล','fas fa-puzzle-piece','{basePath}/modules',10,TRUE,FALSE,NULL),
        ('AreaAdmin','profile','โปรไฟล์','fas fa-id-card','{basePath}/profile',11,TRUE,FALSE,NULL),
        ('SchoolAdmin','dashboard','ภาพรวมโรงเรียน','fas fa-chart-pie','{basePath}',1,TRUE,TRUE,NULL),
        ('SchoolAdmin','profile','ข้อมูลโรงเรียน','fas fa-id-card','{basePath}/profile',2,TRUE,FALSE,NULL),
        ('SchoolAdmin','personnel','บุคลากร','fas fa-user-tie','{basePath}/personnel',3,TRUE,FALSE,NULL),
        ('SchoolAdmin','users','จัดการผู้ใช้','fas fa-users-cog','{basePath}/users',4,TRUE,FALSE,NULL),
        ('SchoolAdmin','delegation','มอบหมายงาน','fas fa-id-badge','{basePath}/personnel/delegation',5,TRUE,FALSE,NULL),
        ('SchoolAdmin','modules','โมดูล','fas fa-puzzle-piece','{basePath}/modules',6,TRUE,FALSE,NULL),
        ('SchoolAdmin','settings','ตั้งค่า','fas fa-cog','{basePath}/settings',7,TRUE,FALSE,NULL),
        ('Teacher','dashboard','หน้าหลัก','fas fa-home','{basePath}',1,TRUE,TRUE,NULL),
        ('Teacher','profile','โปรไฟล์','fas fa-id-card','{basePath}/profile',2,TRUE,FALSE,NULL),
        ('Teacher','my-modules','โมดูลของฉัน','fas fa-puzzle-piece','{basePath}/my-modules',3,TRUE,FALSE,NULL),
        ('Student','dashboard','หน้าหลัก','fas fa-home','{basePath}',1,TRUE,TRUE,NULL),
        ('Student','profile','โปรไฟล์','fas fa-id-card','{basePath}/profile',2,TRUE,FALSE,NULL),
        ('Student','grades','ผลการเรียน','fas fa-star','{basePath}/grades',3,TRUE,FALSE,NULL),
        ('Student','activities','กิจกรรม','fas fa-running','{basePath}/activities',4,TRUE,FALSE,NULL),
        ('Student','health','สุขภาพ','fas fa-heartbeat','{basePath}/health',5,TRUE,FALSE,NULL),
        ('Student','my-modules','โมดูลของฉัน','fas fa-puzzle-piece','{basePath}/my-modules',6,TRUE,FALSE,NULL),
        ('Guest','home','หน้าแรก','fas fa-home','/',1,TRUE,TRUE,'linear-gradient(135deg,#38bdf8,#818cf8)'),
        ('Guest','school-info','ข้อมูลทั่วไป','fas fa-school','/school-info',2,TRUE,FALSE,'linear-gradient(135deg,#fbbf24,#f97316)'),
        ('Guest','student-info','ข้อมูลนักเรียน','fas fa-user-graduate','/student-info',3,TRUE,FALSE,'linear-gradient(135deg,#34d399,#10b981)'),
        ('Guest','personnel-info','ข้อมูลบุคลากร','fas fa-users','/personnel-info',4,TRUE,FALSE,'linear-gradient(135deg,#a78bfa,#8b5cf6)'),
        ('Guest','budget-info','ข้อมูลงบประมาณ','fas fa-coins','/budget-info',5,TRUE,FALSE,'linear-gradient(135deg,#f472b6,#ec4899)'),
        ('Guest','academic-info','ข้อมูลวิชาการ','fas fa-graduation-cap','/academic-info',6,TRUE,FALSE,'linear-gradient(135deg,#818cf8,#6366f1)'),
        ('Guest','analytics','วิเคราะห์','fas fa-chart-line','/analytics',7,TRUE,FALSE,'linear-gradient(135deg,#38bdf8,#0ea5e9)'),
        ('Guest','statistics','สถิติ/บริการ','fas fa-chart-bar','/statistics',8,TRUE,FALSE,'linear-gradient(135deg,#fbbf24,#d97706)'),
        ('Guest','committee','คณะกรรมการ','fas fa-people-group','/committee',9,TRUE,FALSE,'linear-gradient(135deg,#34d399,#059669)')
        ON CONFLICT (""Role"",""ItemKey"") DO UPDATE SET
            ""Label""         = EXCLUDED.""Label"",
            ""Icon""          = EXCLUDED.""Icon"",
            ""RouteTemplate"" = EXCLUDED.""RouteTemplate"",
            ""SortOrder""     = EXCLUDED.""SortOrder"",
            ""ExactMatch""    = EXCLUDED.""ExactMatch"",
            ""Gradient""      = EXCLUDED.""Gradient""
        -- IsActive is intentionally NOT updated: admins can toggle items via menu-management
        ");
    Console.WriteLine("[Seed] MenuItems table ensured.");

    // Invalidate Redis menu caches so the updated seed is served immediately on first request
    try
    {
        var redis = app.Services.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();
        var redisDb = redis.GetDatabase();
        await redisDb.KeyDeleteAsync(new StackExchange.Redis.RedisKey[]
        {
            "menu:core:SuperAdmin", "menu:core:AreaAdmin", "menu:core:SchoolAdmin",
            "menu:core:Teacher",    "menu:core:Student",   "menu:core:Guest",
        });
        Console.WriteLine("[Seed] Menu Redis caches invalidated.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seed] Redis menu cache invalidation skipped: {ex.Message}");
    }

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

    // Seed demo personnel for school 159 (for AI / QA testing)
    await Gateway.PersonnelSeedData.SeedAsync(db);

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
        ("user.manage_school_users",
         "อนุญาตให้ผู้บริหารสถานศึกษาจัดการบัญชีผู้ใช้งานในโรงเรียนของตนเอง"),
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
