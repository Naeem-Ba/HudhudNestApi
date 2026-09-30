using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Marketing.Entities;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Tests.Marketing;

public sealed class MarketingEventTests
{
    [Fact]
    public void Create_WithMinimalData_Succeeds()
    {
        var evt = MarketingEvent.Create(MarketingEventType.PageView, "landing-page");

        Assert.Equal(MarketingEventType.PageView, evt.EventType);
        Assert.Equal("landing-page", evt.Source);
        Assert.Null(evt.SessionId);
        Assert.Null(evt.LeadId);
    }

    [Fact]
    public void Create_WithoutSource_Throws()
    {
        Assert.Throws<DomainException>(() => MarketingEvent.Create(MarketingEventType.PageView, ""));
    }

    [Fact]
    public void Create_WithFullContext_StoresAllFields()
    {
        var leadId = Guid.NewGuid();
        var offerId = Guid.NewGuid();

        var evt = MarketingEvent.Create(
            MarketingEventType.FormComplete,
            "landing-page",
            campaign: "spring-2026",
            sessionId: "sess-abc123",
            path: "/landing#waitlist-form",
            leadId: leadId,
            offerId: offerId);

        Assert.Equal("spring-2026", evt.Campaign);
        Assert.Equal("sess-abc123", evt.SessionId);
        Assert.Equal(leadId, evt.LeadId);
        Assert.Equal(offerId, evt.OfferId);
    }
}
