using System.Text;
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

// Add DbContext (read-only)
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL") ?? throw new InvalidOperationException("PostgreSQL connection string not configured");
builder.Services.AddDbContext<SbdDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

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
});

var app = builder.Build();

// Apply pending migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SbdDbContext>();
    await db.Database.MigrateAsync();
    Console.WriteLine("[Migration] Database schema is up to date.");

    if (!await db.Modules.AnyAsync())
    {
        db.Modules.AddRange(
            new SBD.Domain.Entities.Module { Code = "core-dashboard",  Name = "Dashboard",            Category = "Core",    Icon = "📊", SortOrder = 1,  IsDefault = true,  IsEnabled = true, Description = "แดชบอร์ดหลักของระบบ" },
            new SBD.Domain.Entities.Module { Code = "user-mgmt",       Name = "จัดการผู้ใช้",          Category = "Core",    Icon = "👥", SortOrder = 2,  IsDefault = true,  IsEnabled = true, Description = "จัดการผู้ใช้งานระบบทั้งหมด" },
            new SBD.Domain.Entities.Module { Code = "school-mgmt",     Name = "จัดการโรงเรียน",        Category = "Core",    Icon = "🏫", SortOrder = 3,  IsDefault = true,  IsEnabled = true, Description = "จัดการข้อมูลโรงเรียนในระบบ" },
            new SBD.Domain.Entities.Module { Code = "area-mgmt",       Name = "จัดการเขตพื้นที่",      Category = "Core",    Icon = "🗺️", SortOrder = 4, IsDefault = true,  IsEnabled = true, Description = "จัดการเขตพื้นที่การศึกษา" },
            new SBD.Domain.Entities.Module { Code = "curriculum",      Name = "หลักสูตรสถานศึกษา",    Category = "Feature", Icon = "📚", SortOrder = 10, IsDefault = false, IsEnabled = true, RoutePath = "curriculum", Description = "ระบบเขียนหลักสูตรสถานศึกษา", AssignableToTeacher = true },
            new SBD.Domain.Entities.Module { Code = "attendance",      Name = "ระบบเช็คชื่อ",         Category = "Feature", Icon = "✅", SortOrder = 11, IsDefault = false, IsEnabled = true, RoutePath = "attendance", Description = "บันทึกการเข้าเรียนรายวัน", AssignableToTeacher = true, AssignableToStudent = true },
            new SBD.Domain.Entities.Module { Code = "gradebook",       Name = "สมุดเกรด",             Category = "Feature", Icon = "📝", SortOrder = 12, IsDefault = false, IsEnabled = true, RoutePath = "gradebook", Description = "บันทึกและคำนวณผลการเรียน", AssignableToTeacher = true, AssignableToStudent = true }
        );
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
