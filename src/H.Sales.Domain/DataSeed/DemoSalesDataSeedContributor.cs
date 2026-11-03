using System;
using System.Threading.Tasks;
using H.Sales.Customers;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace H.Sales.DataSeed;

public class DemoSalesDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<Customer, Guid> _customers;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;

    public DemoSalesDataSeedContributor(
        IRepository<Customer, Guid> customers,
        IGuidGenerator guids,
        ICurrentTenant currentTenant)
    {
        _customers     = customers;
        _guids         = guids;
        _currentTenant = currentTenant;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId == null) return; // host: nothing
        using (_currentTenant.Change(context.TenantId))
        {
            if (await _customers.AnyAsync()) return;

            var customer = new Customer(_guids.Create())
            {
                TenantId = context.TenantId,
                Code     = "CUS001",
                Name     = "العميل الافتراضي",
                IsActive = true
            };
            customer.EnsureValid();
            await _customers.InsertAsync(customer);
        }
    }
}
