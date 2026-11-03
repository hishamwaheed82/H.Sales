using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace H.Sales.EntityFrameworkCore;

/// <summary>
/// Used only by <c>dotnet ef</c>. It reads the migrator's configuration so the
/// design-time connection string never has to be duplicated here, and resolves
/// it relative to this project so the tool works from any working directory.
/// </summary>
public class SalesDbContextFactory : IDesignTimeDbContextFactory<SalesDbContext>
{
    public SalesDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();

        var builder = new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer(configuration.GetConnectionString(SalesDbProperties.ConnectionStringName));

        return new SalesDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var migratorPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "H.Sales.DbMigrator");

        return new ConfigurationBuilder()
            .SetBasePath(Path.GetFullPath(migratorPath))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.secrets.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}
