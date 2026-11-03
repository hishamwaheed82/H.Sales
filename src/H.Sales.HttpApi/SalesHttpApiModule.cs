using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace H.Sales;

[DependsOn(
    typeof(SalesApplicationContractsModule),
    typeof(AbpAspNetCoreMvcModule)
)]
public class SalesHttpApiModule : AbpModule
{
}

