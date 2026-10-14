using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Bookings.Commands.CancelVisit;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Bookings.Entities;
using HudhudNestApi.Domain.Bookings.Enums;

namespace HudhudNestApi.Application.Tests.Bookings;

/// <summary>
/// QA phase 6 (P6-D03): cancelling somebody else's visit used to surface the domain rule as a
/// 400 business-rule violation; every other visit command answers a non-party with 403.
/// </summary>
public sealed class CancelVisitAuthorizationTests
{
    [Fact]
    public async Task Cancel_ByNonRequester_IsForbidden_AndDoesNotSave()
    {
        var requesterId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var visit = VisitRequest.Create(
            propertyId, requesterId, DateTime.UtcNow.AddDays(1), "زائر", "0999999999", null);

        var visits = new Mock<IVisitRepository>();
        visits.Setup(x => x.GetByIdAsync(visit.Id, It.IsAny<CancellationToken>())).ReturnsAsync(visit);
        var properties = new Mock<IPropertyReadRepository>();
        properties.Setup(x => x.GetByIdAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertySummary(propertyId, "شقة", "دمشق", Guid.NewGuid(), IsPublished: true, MainImageUrl: null));
        var uow = new Mock<IUnitOfWork>();

        var handler = new CancelVisitCommandHandler(
            visits.Object, uow.Object, Mock.Of<INotificationService>(), properties.Object,
            NullLogger<CancelVisitCommandHandler>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CancelVisitCommand(visit.Id, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(VisitStatus.Pending, visit.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
