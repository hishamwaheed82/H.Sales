using H.Sales.Invoices;
using H.Sales.Customers;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace H.Sales.EntityFrameworkCore;

public interface ISalesDbContext : IEfCoreDbContext
{
    DbSet<Customer>         Customers    { get; }
    DbSet<SalesInvoice>     Invoices     { get; }
    DbSet<SalesInvoiceLine> InvoiceLines { get; }
}
