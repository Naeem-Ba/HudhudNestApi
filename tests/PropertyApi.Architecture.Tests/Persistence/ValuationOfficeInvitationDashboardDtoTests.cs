using System.Reflection;
using PropertyApi.Application.Valuation.DTOs;

namespace PropertyApi.Architecture.Tests.Persistence;

/// <summary>
/// Stage 6's privacy rule, pinned as a structural guard rather than only a behavioural one:
/// the office-facing dashboard DTO must never carry ValuationInquiry.RequesterId or any
/// requester-identifying field, so a future edit that innocently adds "just one more field"
/// to this DTO cannot silently leak who is asking before any consent step exists. Same
/// discipline this codebase's LeadDto/AgencyInvitationDto doc comments describe in prose —
/// this test makes the Valuation module's own version of it fail loudly instead.
/// </summary>
public sealed class ValuationOfficeInvitationDashboardDtoTests
{
    [Fact]
    public void DashboardDto_NeverExposesRequesterIdentity()
    {
        var properties = typeof(ValuationOfficeInvitationDashboardDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        var forbidden = new[] { "RequesterId", "RequesterName", "RequesterEmail", "RequesterPhone", "ContactEmail", "ContactPhone" };

        foreach (var propertyName in properties)
        {
            Assert.DoesNotContain(propertyName, forbidden);
        }
    }
}
