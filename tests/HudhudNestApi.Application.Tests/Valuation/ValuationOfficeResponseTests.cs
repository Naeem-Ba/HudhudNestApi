using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Valuation.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Valuation;

public sealed class ValuationOfficeResponseTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithPositivePrice_Succeeds()
    {
        var invitationId = Guid.NewGuid();

        var response = ValuationOfficeResponse.Create(invitationId, 250_000m, UtcNow);

        Assert.Equal(invitationId, response.InvitationId);
        Assert.Equal(250_000m, response.EstimatedPrice);
        Assert.Equal(UtcNow, response.SubmittedAt);
    }

    [Fact]
    public void Create_WithZeroPrice_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationOfficeResponse.Create(
            Guid.NewGuid(), 0m, UtcNow));
    }

    [Fact]
    public void Create_WithNegativePrice_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationOfficeResponse.Create(
            Guid.NewGuid(), -1m, UtcNow));
    }

    [Fact]
    public void Create_WithEmptyInvitationId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationOfficeResponse.Create(
            Guid.Empty, 250_000m, UtcNow));
    }

    [Fact]
    public void Create_WithoutNotes_Succeeds()
    {
        var response = ValuationOfficeResponse.Create(Guid.NewGuid(), 100_000m, UtcNow);
        Assert.Null(response.Notes);
    }

    [Fact]
    public void Create_WithWhitespaceNotes_NormalizesToNull()
    {
        var response = ValuationOfficeResponse.Create(
            Guid.NewGuid(), 100_000m, UtcNow, notes: "   ");

        Assert.Null(response.Notes);
    }

    [Fact]
    public void Create_WithNotes_TrimsAndStoresThem()
    {
        var response = ValuationOfficeResponse.Create(
            Guid.NewGuid(), 100_000m, UtcNow, notes: "  looks well-maintained  ");

        Assert.Equal("looks well-maintained", response.Notes);
    }
}
