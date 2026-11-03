using H.Sales.Localization;
using Volo.Abp.Application.Services;

namespace H.Sales;

public abstract class SalesAppService : ApplicationService
{
    protected SalesAppService()
    {
        LocalizationResource = typeof(SalesResource);
    }
}
