using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Valuation.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Valuation;

/// <summary>Stage 9 — ValuationContactConsent's own domain invariants.</summary>
public sealed class ValuationContactConsentTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithPhoneOnly_Succeeds()
    {
        var inquiryId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();

        var consent = ValuationContactConsent.Create(inquiryId, invitationId, agencyId, "0991234567", null, UtcNow);

        Assert.Equal(inquiryId, consent.InquiryId);
        Assert.Equal(invitationId, consent.InvitationId);
        Assert.Equal(agencyId, consent.AgencyId);
        Assert.Equal("0991234567", consent.ContactPhone);
        Assert.Null(consent.ContactEmail);
        Assert.Equal(UtcNow, consent.ConsentedAt);
    }

    [Fact]
    public void Create_WithEmailOnly_Succeeds()
    {
        var consent = ValuationContactConsent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "customer@example.test", UtcNow);

        Assert.Null(consent.ContactPhone);
        Assert.Equal("customer@example.test", consent.ContactEmail);
    }

    [Fact]
    public void Create_WithBothPhoneAndEmail_Succeeds()
    {
        var consent = ValuationContactConsent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "0991234567", "customer@example.test", UtcNow);

        Assert.Equal("0991234567", consent.ContactPhone);
        Assert.Equal("customer@example.test", consent.ContactEmail);
    }

    [Fact]
    public void Create_WithNeitherPhoneNorEmail_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationContactConsent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, UtcNow));
    }

    [Fact]
    public void Create_WithBlankPhoneAndBlankEmail_Throws()
    {
        // Whitespace-only counts as "not provided" -- same normalization
        // ValuationOfficeResponse.Notes already applies to its own optional text field.
        Assert.Throws<DomainException>(() => ValuationContactConsent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "   ", "  ", UtcNow));
    }

    [Fact]
    public void Create_TrimsPhoneAndEmail()
    {
        var consent = ValuationContactConsent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  0991234567  ", "  customer@example.test  ", UtcNow);

        Assert.Equal("0991234567", consent.ContactPhone);
        Assert.Equal("customer@example.test", consent.ContactEmail);
    }

    [Fact]
    public void Create_WithEmptyInquiryId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationContactConsent.Create(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "0991234567", null, UtcNow));
    }

    [Fact]
    public void Create_WithEmptyInvitationId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationContactConsent.Create(
            Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "0991234567", null, UtcNow));
    }

    [Fact]
    public void Create_WithEmptyAgencyId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationContactConsent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "0991234567", null, UtcNow));
    }
}
