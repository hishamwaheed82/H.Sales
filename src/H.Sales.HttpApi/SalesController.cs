using H.Sales.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace H.Sales;

public abstract class SalesController : AbpControllerBase
{
    protected SalesController()
    {
        LocalizationResource = typeof(SalesResource);
    }
}
