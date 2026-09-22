using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Domain.Marketing.Entities;

/// <summary>
/// A short, progressive willingness-to-pay survey response from the landing page. Every
/// answer field is optional by design — the survey must stay short and skippable
/// (product requirement: "لا تجعل النموذج طويلًا أو مزعجًا"), so only <see cref="Source"/>
/// (and, transitively, submitting at all) is required. An all-null response is still a
/// valid row: it records that someone opened the survey and chose to submit without
/// answering, which is itself a signal (see MarketingEventType.SurveyStart for the
/// "opened it and abandoned" half of that signal).
/// </summary>
public sealed class SurveyResponse : BaseEntity
{
    /// <summary>Links back to the Lead this respondent already submitted, if any — the
    /// survey is normally shown right after a successful waitlist signup. Null is valid:
    /// nothing requires a Lead to exist first.</summary>
    public Guid? LeadId { get; private set; }

    public PaymentWillingness? WillingnessToPay { get; private set; }
    public PreferredPaymentModel? PreferredPaymentModel { get; private set; }
    public decimal? ExpectedMonthlyPriceUsd { get; private set; }
    public decimal? ExpectedPerListingPriceUsd { get; private set; }
    public decimal? AcceptableCommissionPercent { get; private set; }
    public string? MostImportantFeature { get; private set; }
    public string? BiggestProblem { get; private set; }
    public string? SubscriptionBlocker { get; private set; }
    public bool? WantsTrialBeforePaying { get; private set; }
    public int? TeamSize { get; private set; }
    public int? PropertyCount { get; private set; }
    public bool? UsesSimilarToolCurrently { get; private set; }
    public string? SimilarToolName { get; private set; }

    public string Source { get; private set; } = string.Empty;

    private SurveyResponse() { }

    public static SurveyResponse Create(
        string source,
        Guid? leadId = null,
        PaymentWillingness? willingnessToPay = null,
        PreferredPaymentModel? preferredPaymentModel = null,
        decimal? expectedMonthlyPriceUsd = null,
        decimal? expectedPerListingPriceUsd = null,
        decimal? acceptableCommissionPercent = null,
        string? mostImportantFeature = null,
        string? biggestProblem = null,
        string? subscriptionBlocker = null,
        bool? wantsTrialBeforePaying = null,
        int? teamSize = null,
        int? propertyCount = null,
        bool? usesSimilarToolCurrently = null,
        string? similarToolName = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new DomainException("مصدر الاستبيان مطلوب.");

        if (expectedMonthlyPriceUsd is < 0)
            throw new DomainException("السعر الشهري المتوقع لا يمكن أن يكون سالبًا.");

        if (expectedPerListingPriceUsd is < 0)
            throw new DomainException("سعر الإعلان المتوقع لا يمكن أن يكون سالبًا.");

        if (acceptableCommissionPercent is < 0 or > 100)
            throw new DomainException("نسبة العمولة المقبولة يجب أن تكون بين 0 و100.");

        if (teamSize is < 0)
            throw new DomainException("حجم الفريق لا يمكن أن يكون سالبًا.");

        if (propertyCount is < 0)
            throw new DomainException("عدد العقارات لا يمكن أن يكون سالبًا.");

        return new SurveyResponse
        {
            Source = source.Trim(),
            LeadId = leadId,
            WillingnessToPay = willingnessToPay,
            PreferredPaymentModel = preferredPaymentModel,
            ExpectedMonthlyPriceUsd = expectedMonthlyPriceUsd,
            ExpectedPerListingPriceUsd = expectedPerListingPriceUsd,
            AcceptableCommissionPercent = acceptableCommissionPercent,
            MostImportantFeature = Truncate(mostImportantFeature, 300),
            BiggestProblem = Truncate(biggestProblem, 500),
            SubscriptionBlocker = Truncate(subscriptionBlocker, 500),
            WantsTrialBeforePaying = wantsTrialBeforePaying,
            TeamSize = teamSize,
            PropertyCount = propertyCount,
            UsesSimilarToolCurrently = usesSimilarToolCurrently,
            SimilarToolName = Truncate(similarToolName, 150)
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
