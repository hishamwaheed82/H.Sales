using H.Sales.Invoices;
using H.Sales.Customers;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace H.Sales.EntityFrameworkCore;

public static class SalesDbContextModelCreatingExtensions
{
    private const string MoneyType = "decimal(18,4)";
    private const string QtyType   = "decimal(18,4)";

    public static void ConfigureSales(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
        var prefix = SalesDbProperties.DbTablePrefix;
        var schema  = SalesDbProperties.DbSchema;

        builder.Entity<Customer>(b =>
        {
            b.ToTable(prefix + "Customers", schema);
            b.ConfigureByConvention();
            b.Property(x => x.AttachCount).HasDefaultValue(0);
            b.Property(x => x.NoteCount).HasDefaultValue(0);
            b.Property(x => x.CommentCount).HasDefaultValue(0);
            b.Property(x => x.Code).IsRequired().HasMaxLength(32);
            b.Property(x => x.Name).IsRequired().HasMaxLength(256);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        });

        builder.Entity<SalesInvoice>(b =>
        {
            b.ToTable(prefix + "SalesInvoices", schema);
            b.ConfigureByConvention();
            b.Property(x => x.AttachCount).HasDefaultValue(0);
            b.Property(x => x.NoteCount).HasDefaultValue(0);
            b.Property(x => x.CommentCount).HasDefaultValue(0);
            b.Property(x => x.Number).IsRequired().HasMaxLength(32);
            b.Property(x => x.JournalNumber).HasMaxLength(64);
            b.Property(x => x.StockDocumentId).HasMaxLength(64);
            b.Property(x => x.SubTotal).HasColumnType(MoneyType);
            b.Property(x => x.TaxTotal).HasColumnType(MoneyType);
            b.Property(x => x.GrandTotal).HasColumnType(MoneyType);
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => new { x.TenantId, x.DocDate, x.Status });
            b.HasIndex(x => new { x.TenantId, x.CustomerId });
            b.HasMany(x => x.Lines)
             .WithOne(x => x.Invoice)
             .HasForeignKey(x => x.InvoiceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SalesInvoiceLine>(b =>
        {
            b.ToTable(prefix + "SalesInvoiceLines", schema);
            b.ConfigureByConvention();
            b.Property(x => x.AttachCount).HasDefaultValue(0);
            b.Property(x => x.NoteCount).HasDefaultValue(0);
            b.Property(x => x.CommentCount).HasDefaultValue(0);
            b.Property(x => x.Description).HasMaxLength(512);
            b.Property(x => x.Qty).HasColumnType(QtyType);
            b.Property(x => x.UnitPrice).HasColumnType(MoneyType);
            b.Property(x => x.LineTotal).HasColumnType(MoneyType);
            b.HasIndex(x => new { x.InvoiceId, x.LineNumber });
        });
    }
}
