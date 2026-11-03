using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace H.Sales.Sales;

public interface ISalesAppService : IApplicationService
{
    Task<SalesBootstrapDto> GetBootstrapAsync();

    Task<CustomerDto> CreateCustomerAsync(CreateUpdateCustomerDto input);
    Task<CustomerDto> UpdateCustomerAsync(Guid id, CreateUpdateCustomerDto input);

    Task<SalesInvoiceDto> CreateInvoiceAsync(CreateSalesInvoiceDto input);
    Task<SalesInvoiceDto> PostInvoiceAsync(Guid id);
}
