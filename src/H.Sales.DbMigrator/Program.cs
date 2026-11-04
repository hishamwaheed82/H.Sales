using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using H.Sales.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace H.Sales.DbMigrator;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        try
        {
            using var application = await AbpApplicationFactory.CreateAsync<SalesDbMigratorModule>(options =>
            {
                options.Services.ReplaceConfiguration(BuildConfiguration());
                options.UseAutofac();
                options.Services.AddLogging(c => c.AddSerilog());
            });

            await application.InitializeAsync();

            using var scope = application.ServiceProvider.CreateScope();
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            using (var uow = uowManager.Begin(requiresNew: true, isTransactional: false))
            {
                var provider = scope.ServiceProvider.GetRequiredService<IDbContextProvider<SalesDbContext>>();
                var dbContext = await provider.GetDbContextAsync();
                Log.Information("Applying Sales migrations...");
                await dbContext.Database.MigrateAsync();
                await uow.CompleteAsync();
            }

            Log.Information("Seeding Sales demo data...");
            var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var seeder = scope.ServiceProvider.GetRequiredService<IDataSeeder>();
            var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();

            using (currentTenant.Change(null))
            using (var uow = uowManager.Begin(requiresNew: true, isTransactional: true))
            {
                await seeder.SeedAsync(new DataSeedContext(tenantId: null));
                await uow.CompleteAsync();
            }

            List<Tenant> tenants;
            using (var uow = uowManager.Begin(requiresNew: true, isTransactional: false))
            {
                tenants = await tenantRepository.GetListAsync();
                await uow.CompleteAsync();
            }

            foreach (var tenant in tenants)
            {
                Log.Information("Seeding Sales demo data for tenant {TenantName} ({TenantId})...", tenant.Name, tenant.Id);
                using (currentTenant.Change(tenant.Id))
                using (var uow = uowManager.Begin(requiresNew: true, isTransactional: true))
                {
                    await seeder.SeedAsync(new DataSeedContext(tenant.Id));
                    await uow.CompleteAsync();
                }
            }

            Log.Information("Sales database is up to date.");
            await application.ShutdownAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Sales migration failed.");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.secrets.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}
