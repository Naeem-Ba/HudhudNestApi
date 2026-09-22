using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Domain.Services.Entities;

/// <summary>
/// One sellable service a ServiceProvider offers within a single category (e.g. "HudhudNest
/// Verify — Standard Document Check"). A ServiceRequest always points at exactly one offering.
///
/// BasePrice/CurrencyId reuse the platform's existing Currency lookup — same FK pattern as
/// SaleDetails/RentalDetails.PriceCurrencyId. Price is nullable: a provider may choose to list
/// "price on request" rather than the codebase inventing a number that was never actually set.
/// </summary>
public sealed class ServiceOffering : AuditableEntity
{
    private ServiceOffering() { }

    public Guid ServiceProviderId { get; private set; }

    public ServiceCategory Category { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public decimal? BasePrice { get; private set; }

    public int? CurrencyId { get; private set; }

    public int? EstimatedDurationDays { get; private set; }

    public bool IsActive { get; private set; } = true;

    // ── Navigation (EF) ───────────────────────────────────────────
    public ServiceProvider? ServiceProvider { get; private set; }

    public static ServiceOffering Create(
        Guid serviceProviderId,
        ServiceCategory category,
        string title,
        string? description,
        decimal? basePrice,
        int? currencyId,
        int? estimatedDurationDays,
        DateTime utcNow)
    {
        if (serviceProviderId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء خدمة بلا مزوّد.");

        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان الخدمة مطلوب.");

        if (basePrice is < 0)
            throw new DomainException("سعر الخدمة لا يمكن أن يكون سالباً.");

        if (basePrice is not null && currencyId is null)
            throw new DomainException("عملة السعر مطلوبة عند تحديد سعر للخدمة.");

        if (estimatedDurationDays is < 0)
            throw new DomainException("مدة تنفيذ الخدمة لا يمكن أن تكون سالبة.");

        return new ServiceOffering
        {
            ServiceProviderId = serviceProviderId,
            Category = category,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            BasePrice = basePrice,
            CurrencyId = currencyId,
            EstimatedDurationDays = estimatedDurationDays,
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    public void UpdateDetails(
        string title,
        string? description,
        decimal? basePrice,
        int? currencyId,
        int? estimatedDurationDays,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان الخدمة مطلوب.");

        if (basePrice is < 0)
            throw new DomainException("سعر الخدمة لا يمكن أن يكون سالباً.");

        if (basePrice is not null && currencyId is null)
            throw new DomainException("عملة السعر مطلوبة عند تحديد سعر للخدمة.");

        if (estimatedDurationDays is < 0)
            throw new DomainException("مدة تنفيذ الخدمة لا يمكن أن تكون سالبة.");

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        BasePrice = basePrice;
        CurrencyId = currencyId;
        EstimatedDurationDays = estimatedDurationDays;
        UpdatedAt = utcNow;
    }

    public void Activate(DateTime utcNow)
    {
        IsActive = true;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }
}
