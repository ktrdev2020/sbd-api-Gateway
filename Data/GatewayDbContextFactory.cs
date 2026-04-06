using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SBD.Infrastructure.Data;

namespace Gateway.Data;

public class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SbdDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=ssk3-sbd-db;Username=postgres;Password=postgres")
            .Options;
        return new GatewayDbContext(options);
    }
}
