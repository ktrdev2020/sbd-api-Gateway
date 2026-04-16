using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SBD.Infrastructure.Data;

namespace Gateway.Data;

/// <summary>
/// Design-time factory for SbdDbContext — used exclusively by `dotnet ef migrations add`
/// when targeting SBD.Infrastructure as the migrations assembly.
///
/// EF CLI picks this factory when `--context SbdDbContext` is specified, and the explicit
/// MigrationsAssembly("SBD.Infrastructure") override ensures the generated migration file
/// lands in libs/dotnet/SBD.Infrastructure/Data/Migrations/ (not in Gateway/Migrations/).
/// </summary>
public class SbdDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SbdDbContext>
{
    public SbdDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SbdDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=ssk3-sbd-db;Username=postgres;Password=postgres",
                b => b.MigrationsAssembly("SBD.Infrastructure"))
            .Options;
        return new SbdDbContext(options);
    }
}
