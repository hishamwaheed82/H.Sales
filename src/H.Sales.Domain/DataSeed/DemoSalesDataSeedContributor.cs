using System;
using System.Threading.Tasks;
using H.Sales.Customers;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace H.Sales.DataSeed;

/// <summary>
/// Idempotent demo seed for a fresh tenant: two customers with contact fields.
/// Skips host; no-ops when any customer already exists (never wipes production).
/// </summary>
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

            var samples = new[]
            {
                ("CUS001", "العميل الافتراضي", "01000000001", "customer1@demo.local"),
                ("CUS002", "محل النور",         "01000000002", "noor@demo.local")
            };

            foreach (var (code, name, phone, email) in samples)
            {
                var customer = new Customer(_guids.Create())
                {
                    TenantId = context.TenantId,
                    Code     = code,
                    Name     = name,
                    Phone    = phone,
                    Email    = email,
                    IsActive = true
                };
                customer.EnsureValid();
                await _customers.InsertAsync(customer);
            }
        }
    }
}
