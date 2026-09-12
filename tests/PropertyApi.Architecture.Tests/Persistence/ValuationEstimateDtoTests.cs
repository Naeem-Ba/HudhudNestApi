using System.Reflection;
using PropertyApi.Application.Valuation.DTOs;

namespace PropertyApi.Architecture.Tests.Persistence;

/// <summary>
/// Stage 10 — "Customer → Agency internal data must never leak" pinned as a structural guard.
/// ValuationEstimateDto is allowed to carry an agency's already-public identity (AgencyId,
/// AgencyName — see the record's own doc comment: an agency's name is not private, it already
/// has its own public profile page). What must never appear on it is any of Agency's actual
/// back-office/internal fields.
/// </summary>
public sealed class ValuationEstimateDtoTests
{
    [Fact]
    public void EstimateDto_NeverExposesAgencyInternalFields()
    {
        var properties = typeof(ValuationEstimateDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        var forbidden = new[]
        {
            "ContactEmail", "ContactPhone", "LicenseNumber", "OwnerUserId",
            "RequiresManualReview", "ManualReviewReason", "ManualReviewFlaggedAt",
            // Also never the requester's OWN identity reflected back with extra detail beyond
            // what the customer already knows about themselves.
            "RequesterId",
        };

        foreach (var propertyName in properties)
        {
            Assert.DoesNotContain(propertyName, forbidden);
        }
    }
}
