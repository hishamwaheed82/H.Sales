using System;
using H.Platform.Common;
using Volo.Abp;

namespace H.Sales.Customers;

/// <summary>
/// Customer master — the external party to whom goods/services are sold.
/// </summary>
public class Customer : HTrackedAggregateRoot<Guid>
{
    /// <summary>Short, unique business code (e.g. "CUS001").</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    protected Customer() { }

    public Customer(Guid id) : base(id) { }

    public void EnsureValid()
    {
        Code = Check.NotNullOrWhiteSpace(Code, nameof(Code), maxLength: 32);
        Name = Check.NotNullOrWhiteSpace(Name, nameof(Name), maxLength: 256);
    }
}
