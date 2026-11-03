using H.Platform;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace H.Sales;

[DependsOn(
    typeof(SalesDomainSharedModule),
    typeof(PlatformDomainModule),
    typeof(AbpDddDomainModule)
)]
public class SalesDomainModule : AbpModule
{
}
