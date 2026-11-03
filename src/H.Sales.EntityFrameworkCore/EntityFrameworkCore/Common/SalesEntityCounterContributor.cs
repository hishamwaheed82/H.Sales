using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using H.Sales.Common;
using H.Platform.EntityAttachments;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace H.Sales.EntityFrameworkCore.Common;

public class SalesEntityCounterContributor : IEntityCounterContributor, ITransientDependency
{
    private readonly IDbContextProvider<SalesDbContext> _db;

    public SalesEntityCounterContributor(IDbContextProvider<SalesDbContext> db)
    {
        _db = db;
    }

    public bool CanHandle(string entityCode) =>
        entityCode is SalesEntityCodes.Customer
            or SalesEntityCodes.SalesInvoice
            or SalesEntityCodes.InvoiceLine;

    public async Task AdjustAsync(
        string entityCode,
        Guid recordId,
        int attachDelta,
        int noteDelta,
        int commentDelta,
        CancellationToken cancellationToken = default)
    {
        var db = await _db.GetDbContextAsync();
        switch (entityCode)
        {
            case SalesEntityCodes.Customer:
                await db.Customers.Where(x => x.Id == recordId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AttachCount, x => x.AttachCount + attachDelta)
                    .SetProperty(x => x.NoteCount, x => x.NoteCount + noteDelta)
                    .SetProperty(x => x.CommentCount, x => x.CommentCount + commentDelta), cancellationToken);
                break;
            case SalesEntityCodes.SalesInvoice:
                await db.Invoices.Where(x => x.Id == recordId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AttachCount, x => x.AttachCount + attachDelta)
                    .SetProperty(x => x.NoteCount, x => x.NoteCount + noteDelta)
                    .SetProperty(x => x.CommentCount, x => x.CommentCount + commentDelta), cancellationToken);
                break;
            case SalesEntityCodes.InvoiceLine:
                await db.InvoiceLines.Where(x => x.Id == recordId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AttachCount, x => x.AttachCount + attachDelta)
                    .SetProperty(x => x.NoteCount, x => x.NoteCount + noteDelta)
                    .SetProperty(x => x.CommentCount, x => x.CommentCount + commentDelta), cancellationToken);
                break;
        }
    }
}
