using H.Sales.Invoices;
using H.Sales.Customers;
using H.Platform.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;

namespace H.Sales.EntityFrameworkCore;

[ConnectionStringName(SalesDbProperties.ConnectionStringName)]
public class SalesDbContext : SoftPlanAbpDbContext<SalesDbContext>, ISalesDbContext
{
    public DbSet<Customer>         Customers    { get; set; } = null!;
    public DbSet<SalesInvoice>     Invoices     { get; set; } = null!;
    public DbSet<SalesInvoiceLine> InvoiceLines { get; set; } = null!;

    public SalesDbContext(DbContextOptions<SalesDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureSales();
    }
}
