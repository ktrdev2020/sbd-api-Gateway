using System.Text;
using Gateway.Data;
using Gateway.Services;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
        ClockSkew = TimeSpan.Zero
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
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "ใส่ JWT token (ไม่ต้องพิมพ์ Bearer นำหน้า)"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});

var app = builder.Build();

// Apply pending migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SbdDbContext>();
    await db.Database.MigrateAsync();
    Console.WriteLine("[Migration] Database schema is up to date.");

    // Apply Gateway-specific schema additions (shadow properties + AreaModuleAssignments)
    // These columns are defined as shadow properties in GatewayDbContext but the migration
    // lives in a different assembly, so we apply them via raw SQL (idempotent).
    await db.Database.ExecuteSqlRawAsync(@"
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

        -- UserSessions table (Presence & Analytics — written by WorkerService via RabbitMQ)
        CREATE TABLE IF NOT EXISTS ""UserSessions"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""SessionId"" VARCHAR(100) NOT NULL,
            ""UserId"" INTEGER REFERENCES ""Users""(""Id"") ON DELETE SET NULL,
            ""IpAddress"" VARCHAR(50),
            ""ConnectedAt"" TIMESTAMPTZ NOT NULL,
            ""DisconnectedAt"" TIMESTAMPTZ,
            ""DurationSeconds"" DOUBLE PRECISION
        );
        CREATE INDEX IF NOT EXISTS ""IX_UserSessions_SessionId"" ON ""UserSessions"" (""SessionId"");
        CREATE INDEX IF NOT EXISTS ""IX_UserSessions_ConnectedAt"" ON ""UserSessions"" (""ConnectedAt"");
    ");
    Console.WriteLine("[Migration] Gateway shadow properties, AreaModuleAssignments, PositionTypes, WorkGroups, AcademicStandingTypes, UserSessions ensured.");

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

    // Seed schools from สพป.ศรีสะเกษ เขต 3
    await Gateway.SchoolSeedData.SeedAsync(db);
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
