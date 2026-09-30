using System.Reflection;
using HudhudNestApi.Application.Valuation.DTOs;

namespace HudhudNestApi.Architecture.Tests.Persistence;

/// <summary>
/// Stage 6's privacy rule, pinned as a structural guard rather than only a behavioural one:
/// the office-facing dashboard DTO must never carry ValuationInquiry.RequesterId or any field
/// that would expose the requester's raw account identity.
///
/// Stage 9 update: ContactPhone/ContactEmail were REMOVED from the forbidden list — this DTO
/// now deliberately carries ContactPhone/ContactEmail properties (renamed
/// CustomerContactPhone/CustomerContactEmail), populated only when a ValuationContactConsent
/// row exists for that exact invitation. The structural guard below narrows to what must still
/// NEVER exist on this type under any circumstance — the requester's raw identity (their
/// account id, name) — while the behavioural guard for the contact fields (null pre-consent,
/// populated post-consent) lives in GetMyAgencyValuationInquiriesQueryHandlerTests, since
/// "does this specific field, on this specific row, correctly depend on consent" is a fact
/// about the handler's logic, not the DTO's shape.
/// </summary>
public sealed class ValuationOfficeInvitationDashboardDtoTests
{
    [Fact]
    public void DashboardDto_NeverExposesRawRequesterIdentity()
    {
        var properties = typeof(ValuationOfficeInvitationDashboardDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        // Deliberately does NOT include ContactPhone/ContactEmail/CustomerContactPhone/
        // CustomerContactEmail (see this test's own doc comment) — those are Stage 9's
        // intentional, consent-gated exception. What must never exist is the requester's raw
        // identity: WHO they are, not what contact channel they consented to share.
        var forbidden = new[] { "RequesterId", "RequesterName" };

        foreach (var propertyName in properties)
        {
            Assert.DoesNotContain(propertyName, forbidden);
        }
    }

    /// <summary>
    /// The DTO's only customer-facing fields are the two explicitly-consent-gated contact
    /// ones — pinned so a future edit that innocently adds "just one more field" (e.g. a
    /// display name, a raw account id) cannot slip through unnoticed.
    /// </summary>
    [Fact]
    public void DashboardDto_CustomerFacingFieldsAreExactlyContactPhoneAndEmail()
    {
        var properties = typeof(ValuationOfficeInvitationDashboardDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => name.StartsWith("Customer", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["CustomerContactEmail", "CustomerContactPhone"], properties);
    }
}
