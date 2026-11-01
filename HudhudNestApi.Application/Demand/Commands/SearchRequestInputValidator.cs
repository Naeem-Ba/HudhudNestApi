using System.Text.RegularExpressions;
using FluentValidation;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Validation;
using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Domain.Demand.Entities;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Demand.Commands;

/// <summary>Rules shared by create and update of a search request.</summary>
public static partial class SearchRequestInputRules
{
    private const int MaxRooms = 50;

    /// <summary>
    /// Advertisers read the details text, so it must not carry a way to reach the requester outside the
    /// platform: a phone-like run of 7+ digits (Latin or Arabic-Indic, spaces and dashes allowed), an e-mail,
    /// a link or a messenger handle.
    /// </summary>
    public static bool ContainsContactDetails(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return ContactPattern().IsMatch(text);
    }

    [GeneratedRegex(@"([0-9٠-٩۰-۹][\s\-\.\(\)]*){7,}|@|https?://|www\.|t\.me|wa\.me|telegram|whatsapp|واتس|تلغرام|تيليجرام",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContactPattern();

    public static void Apply<T>(AbstractValidator<T> v, ILocationHierarchyChecker locations, ICommonLookupService lookups)
        where T : ISearchRequestInput
    {
        v.RuleFor(x => x.ListingType)
            .Must(t => t is ListingType.ForSale or ListingType.ForRent)
            .WithMessage("نوع الطلب يجب أن يكون شراء أو إيجار.");

        v.RuleFor(x => x.GovernorateId).GreaterThan(0).WithMessage("المحافظة مطلوبة.");

        v.RuleFor(x => x)
            .MustAsync((x, ct) => locations.IsConsistentAsync(x.GovernorateId, x.DistrictId, x.NeighborhoodId, ct))
            .When(x => x.GovernorateId > 0)
            .WithName("Location")
            .WithMessage("الموقع المحدد غير صحيح.");

        v.RuleFor(x => x.PropertyTypeId)
            .MustAsync(async (id, ct) => (await lookups.GetPropertyTypeCatalogAsync(ct)).Any(t => t.Id == id))
            .When(x => x.PropertyTypeId.HasValue)
            .WithMessage("نوع العقار غير معروف.");

        v.RuleFor(x => x.CurrencyCode)
            .NotEmpty()
            .Must(SearchCurrencies.IsSupported)
            .WithMessage(SearchCurrencies.UnsupportedMessage);

        v.RuleFor(x => x.MaxBudget).GreaterThan(0).WithMessage("الميزانية القصوى مطلوبة.").WithinMoneyRange();
        v.RuleFor(x => x.MinBudget).GreaterThanOrEqualTo(0).When(x => x.MinBudget.HasValue).WithinMoneyRange();
        v.RuleFor(x => x.MinBudget)
            .LessThanOrEqualTo(x => x.MaxBudget)
            .When(x => x.MinBudget.HasValue)
            .WithMessage("الحد الأدنى للميزانية يجب ألا يتجاوز الحد الأقصى.");

        v.RuleFor(x => x.MinRooms).InclusiveBetween(0, MaxRooms).When(x => x.MinRooms.HasValue);
        v.RuleFor(x => x.MinArea).GreaterThanOrEqualTo(0).When(x => x.MinArea.HasValue).WithinAreaRange();
        v.RuleFor(x => x.MaxArea).GreaterThan(0).When(x => x.MaxArea.HasValue).WithinAreaRange();
        v.RuleFor(x => x.MaxArea)
            .GreaterThanOrEqualTo(x => x.MinArea!.Value)
            .When(x => x.MinArea.HasValue && x.MaxArea.HasValue)
            .WithMessage("الحد الأقصى للمساحة يجب ألا يقل عن الحد الأدنى.");

        v.RuleFor(x => x.Details)
            .MaximumLength(PropertySearchRequest.DetailsMaxLength)
            .NoNulCharacters()
            .Must(d => !ContainsContactDetails(d))
            .WithErrorCode("SEARCH_REQUEST_CONTACT_IN_DETAILS")
            .WithMessage("لا تكتب رقم هاتف أو بريدًا أو رابطًا في التفاصيل. يصلك المعلنون عبر المنصة دون كشف بياناتك.")
            .When(x => x.Details is not null);

        v.RuleFor(x => x.ExpiresAt)
            .Must(e => e!.Value.ToUniversalTime() > DateTime.UtcNow)
            .When(x => x.ExpiresAt.HasValue)
            .WithMessage("تاريخ انتهاء الطلب يجب أن يكون في المستقبل.");
    }
}

public sealed class CreateSearchRequestCommandValidator : AbstractValidator<CreateSearchRequestCommand>
{
    public CreateSearchRequestCommandValidator(ILocationHierarchyChecker locations, ICommonLookupService lookups)
        => SearchRequestInputRules.Apply(this, locations, lookups);
}

public sealed class UpdateSearchRequestCommandValidator : AbstractValidator<UpdateSearchRequestCommand>
{
    public UpdateSearchRequestCommandValidator(ILocationHierarchyChecker locations, ICommonLookupService lookups)
        => SearchRequestInputRules.Apply(this, locations, lookups);
}
