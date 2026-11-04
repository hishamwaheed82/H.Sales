namespace H.Sales;

public static class SALESErrorCodes
{
    public const string DuplicateCustomerCode   = "SALES:40001";
    public const string InvoiceNotDraft         = "SALES:40002";
    public const string InvoiceAlreadyPosted    = "SALES:40003";
    public const string CustomerNotFound        = "SALES:40004";
    public const string InvoiceNotFound         = "SALES:40005";
    public const string NoLines                 = "SALES:40006";
    public const string InvalidQuantity         = "SALES:40007";
    public const string InvalidUnitCost         = "SALES:40008";
    public const string InvoiceNotCancellable   = "SALES:40009";
}

