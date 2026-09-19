using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Database;

public sealed class DesignTimeWmsDbContextFactory : IDesignTimeDbContextFactory<WmsDbContext>
{
    public WmsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("WARECOMMAND_POSTGRES_CONNECTION")
                               ?? "Host=localhost;Database=warecommand";

        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(WmsDbContext).Assembly.FullName))
            .Options;

        return new WmsDbContext(options);
    }
}
