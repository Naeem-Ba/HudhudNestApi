using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace PropertyApi.Architecture.Tests.Api;

/// <summary>
/// Guards the default-deny decision made in Program.cs.
///
/// Program.cs sets an authorization FallbackPolicy, so an endpoint carrying neither
/// [Authorize] nor [AllowAnonymous] returns 401 rather than being quietly public. That
/// is only half the protection: the other half is that publishing an endpoint has to be
/// a decision somebody wrote down, not something that happens by leaving an attribute
/// off. These tests fail the build when a new endpoint has no explicit answer, and they
/// pin the exact set of endpoints reachable without credentials, so widening the public
/// surface cannot pass review unnoticed.
/// </summary>
public sealed class PublicEndpointPolicyTests
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    /// <summary>
    /// Every endpoint that answers without a token today. Adding a line here is the
    /// deliberate act of publishing an endpoint; the test below fails until it happens,
    /// and a reviewer sees the addition in the diff.
    /// </summary>
    private static readonly HashSet<string> ApprovedAnonymousEndpoints =
        new(StringComparer.Ordinal)
        {
            // Authentication: callers cannot hold a token yet.
            "AuthController.Register",
            "AuthController.Login",
            "AuthController.ForgotPassword",
            "AuthController.ResetPassword",
            "AuthController.Refresh",
            "AuthController.SocialLoginGoogle",
            "AuthController.SocialLoginApple",
            "PhoneAuthController.LegacyPhoneOtpFlowRemoved",
            "PhoneAuthController.VerifyEmail",

            // Resending a confirmation link: the caller is stuck precisely because they
            // cannot confirm, so they hold no token. The response is identical for known
            // and unknown addresses, and the "auth-password-reset" rate-limit policy
            // bounds abuse.
            "PhoneAuthController.ResendConfirmation",

            "PhonePasswordAuthController.SendRegistrationOtp",
            "PhonePasswordAuthController.Register",
            "PhonePasswordAuthController.Login",
            "PhonePasswordAuthController.SendReset",
            "PhonePasswordAuthController.VerifyReset",
            "PhonePasswordAuthController.ConfirmReset",
            "CsrfController.GetCsrfToken",

            // Public catalogue: the listings a visitor browses before signing up.
            "PropertiesController.GetAll",
            "PropertiesController.SearchNearby",
            "PropertiesController.GetById",

            // Social Sharing & Distribution: recording a successful share/copy-link event.
            // Most visitors sharing a listing are not logged in, so this cannot require
            // authentication — abuse is bounded by "property-share-events" rate limiting, and
            // the handler independently re-checks the listing is still public before writing
            // anything (see TrackPropertyShareEventCommandHandler).
            "PropertiesController.TrackShareEvent",

            // UTM / Attribution (Phase 2): recording a property view or a contact/lead action
            // that followed a (possibly attributed) link — same rationale as TrackShareEvent
            // above; bounded by "property-attribution-events" rate limiting, and the handler
            // independently re-checks the listing is still public before writing anything.
            "PropertiesController.TrackAttributionEvent",

            // Short-Stay Accommodation: the search/detail pages a guest browses before
            // booking, and a unit's public availability calendar — all anonymous, read-only,
            // and rate-limited under "shortstay-search".
            "ShortStayListingsController.Search",
            "ShortStayListingsController.GetById",
            "ShortStayBookingsController.GetAvailability",
            "ShortStayBookingsController.GetPricingPreview",
            "PropertyImagesController.GetAll",
            "ReviewsController.GetPropertyReviews",
            "AnalyticsController.GetMarketInsights",

            // Public agency page: the office behind a listing, same rationale as the
            // public seller profile below. Members are shown by display name and avatar
            // only — no contact details, no email addresses.
            "AgenciesController.GetBySlug",

            // Public seller profile: "who am I about to contact?".
            "UsersController.GetProfile",
            "UsersController.GetRatings",

            // Reference data used to render search filters and forms.
            "AmenitiesController.GetAll",
            "EnumController.Get",
            "EnumController.GetAll",
            "LookupsController.GetAmenities",
            "LookupsController.GetCategories",
            "LookupsController.GetPropertyTypes",
            "LookupsController.GetCities",
            "LookupsController.GetGovernorates",
            "LookupsController.GetDistricts",
            "LookupsController.GetNeighborhoods",
            "LookupsController.GetPropertyTypeCatalog",

            // Public plan catalog for the /pricing marketing page — FRONTEND_BACKEND_CONTRACT.md §11.2.
            "PlansController.GetPlans",

            // AqarTech Services Marketplace: the public marketplace browse surface — a visitor
            // compares services/providers before signing up, same rationale as the property
            // catalogue above.
            "ServiceOfferingsController.GetCategories",
            "ServiceOfferingsController.GetByCategory",
            "ServiceOfferingsController.GetById",
            "ServiceProvidersController.GetReviews",

            // Public contact form; abuse is bounded by the "contact" rate-limit policy.
            "ContactController.Submit",

            // Landing-page waitlist / lead capture; abuse is bounded by "leads-submit".
            "LeadsController.Submit",

            // Public "current offer" card (e.g. "first 100 agencies") on the marketing
            // landing page — read-only, bounded by "public-read". No billing/payment
            // gateway is involved (FRONTEND_BACKEND_CONTRACT.md §11.5).
            "OffersController.GetActive",

            // Landing-page willingness-to-pay survey; abuse is bounded by "surveys-submit".
            "SurveysController.SubmitLanding",

            // Marketing conversion-funnel tracking (page view, CTA click, ...); abuse is
            // bounded by "marketing-events".
            "MarketingEventsController.Track",

            // Investment Discovery Portal (Phase 1): the projects a visitor browses/analyzes
            // before signing up, same rationale as the property catalogue above. Every one of
            // these is read-only (or, for the calculator, a stateless indicative computation)
            // and rate-limited under "public-read".
            "InvestmentsController.GetProjects",
            "InvestmentsController.GetProjectById",
            "InvestmentsController.GetProjectFinancials",
            "InvestmentsController.GetProjectRisk",
            "InvestmentsController.GetProjectDocuments",
            "InvestmentsController.GetProjectUpdates",
            "InvestmentsController.Calculate",

            // App Update Management (Phase 1): a client must be able to ask "is my version
            // still supported?" before it can prove who it is, and the response carries no
            // sensitive data (just version/store-url/release-notes metadata) — read-only,
            // bounded by "public-read".
            "AppUpdatesController.Check",

            // Operational surface.
            "ObservabilitySyntheticController.Execute",
            "ObservabilityAlertTestController.SetAlertTestState",

            // Valuation Stage 7: a customer submitting/checking a valuation request is
            // frequently anonymous by design (ValuationInquiry.RequesterId is nullable
            // specifically for a guest visitor). GetStatus independently re-checks ownership
            // whenever the inquiry actually belongs to an account
            // (GetValuationInquiryStatusQueryHandler) — an anonymous inquiry's own
            // hard-to-guess Guid id is its only access control, the same reliance this
            // module's Fast Path already places on the id alone for correlation.
            "ValuationInquiriesController.Create",
            "ValuationInquiriesController.GetStatus",

            // Stage 9: the customer's explicit "let this office contact me" action — anonymous
            // for the same reason (a guest inquiry has no account to authenticate as); the
            // handler independently re-checks inquiry ownership exactly like GetStatus does.
            "ValuationInquiriesController.SubmitContactConsent",

            // Staging-only, and gated on their own checks rather than on a JWT.
            // BuildInfo moved here in RELEASE-BLOCKERS-AR.md B-8 — it used to answer anyone
            // with real infrastructure detail (migration state, PostGIS availability, Staging's
            // isolation markers); it now shares StagingTestSupportAuthorization's secret gate.
            "OperationalController.BuildInfo",
            "StagingTestSupportController.GetMedia",
            "StagingTestSupportController.Cleanup"
        };

    [Fact]
    public void EveryEndpoint_Should_Declare_Authorize_Or_AllowAnonymous()
    {
        var undeclared = Endpoints()
            .Where(endpoint => !HasAllowAnonymous(endpoint) && !HasAuthorize(endpoint))
            .Select(Describe)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            "These endpoints declare neither [Authorize] nor [AllowAnonymous]. The " +
            "FallbackPolicy in Program.cs means they now return 401, which may or may " +
            "not be what you intended -- say which one it is on the action or its " +
            "controller:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, undeclared));
    }

    [Fact]
    public void AnonymousEndpoints_Should_Match_TheApprovedList()
    {
        var actual = Endpoints()
            .Where(HasAllowAnonymous)
            .Select(Describe)
            .ToHashSet(StringComparer.Ordinal);

        var newlyPublic = actual.Except(ApprovedAnonymousEndpoints).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var noLongerPublic = ApprovedAnonymousEndpoints.Except(actual).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(
            newlyPublic.Count == 0,
            "These endpoints became reachable without authentication. If that is " +
            "intended, add them to ApprovedAnonymousEndpoints so the change is visible " +
            "in the diff:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, newlyPublic));

        Assert.True(
            noLongerPublic.Count == 0,
            "These endpoints are on the approved-anonymous list but are no longer " +
            "anonymous (or no longer exist). Remove the stale entries:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, noLongerPublic));
    }

    private static IEnumerable<MethodInfo> Endpoints()
        => ApiAssembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

    private static bool HasAllowAnonymous(MethodInfo endpoint)
        => endpoint.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null ||
           endpoint.DeclaringType!.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;

    private static bool HasAuthorize(MethodInfo endpoint)
        => endpoint.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is not null ||
           endpoint.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is not null;

    private static string Describe(MethodInfo endpoint)
        => $"{endpoint.DeclaringType!.Name}.{endpoint.Name}";
}
