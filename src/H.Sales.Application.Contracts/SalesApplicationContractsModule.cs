using H.Platform;
using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace H.Sales;

[DependsOn(
    typeof(SalesDomainSharedModule),
    typeof(PlatformApplicationContractsModule),
    typeof(AbpDddApplicationContractsModule)
)]
public class SalesApplicationContractsModule : AbpModule
{
}
