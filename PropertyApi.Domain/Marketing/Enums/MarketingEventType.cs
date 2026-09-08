namespace PropertyApi.Domain.Marketing.Enums;

/// <summary>
/// Conversion-funnel events tracked on marketing surfaces (today: the landing page).
/// Deliberately does NOT include "checkout started" / "subscription completed" — no
/// payment gateway or billing system exists yet (FRONTEND_BACKEND_CONTRACT.md §11.5), and
/// logging events for a flow that doesn't exist would misrepresent real usage the moment
/// anyone reads this table. Add those only once the flows they describe are real.
/// </summary>
public enum MarketingEventType
{
    PageView = 1,
    CtaClick = 2,
    FormStart = 3,
    FormComplete = 4,
    PricingView = 5,
    PlanSelect = 6,
    SurveyStart = 7,
    SurveySubmit = 8,
    DemoRequest = 9
}
