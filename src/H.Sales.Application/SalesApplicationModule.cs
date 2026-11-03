using H.Platform;
using H.Sales.Gl;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;

namespace H.Sales;

[DependsOn(
    typeof(SalesDomainModule),
    typeof(SalesApplicationContractsModule),
    typeof(PlatformApplicationModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpAutoMapperModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpIdentityApplicationModule)
)]
public class SalesApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<SalesApplicationModule>(validate: false);
        });

        // ── SALES → GL options ────────────────────────────────────────────
        context.Services.Configure<SalesGlOptions>(configuration.GetSection(SalesGlOptions.SectionName));

        // ── Shared HttpClients ────────────────────────────────────────────────
        context.Services.AddHttpClient("H.Auth");
        context.Services.AddHttpClient("H.Accounting.GL");

        // ── SALES → GL services ───────────────────────────────────────────
        context.Services.AddSingleton<AccountingGlClient>();
        context.Services.AddScoped<SalesGlPoster>();

        context.Services.AddMemoryCache();
    }
}

