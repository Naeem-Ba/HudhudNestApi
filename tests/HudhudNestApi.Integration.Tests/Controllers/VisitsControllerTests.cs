using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using HudhudNestApi.Application.Bookings.Commands.AcceptRescheduledVisit;
using HudhudNestApi.Application.Bookings.Commands.DeclineRescheduledVisit;
using HudhudNestApi.Application.Bookings.Commands.ProposeAlternateVisit;
using HudhudNestApi.Controllers;

namespace HudhudNestApi.Integration.Tests.Controllers;

/// <summary>
/// Controller-level tests (mocked ISender, no HTTP host) — same style as
/// ServiceRequestsControllerTests. Covers the three reschedule endpoints
/// fix/visit-request-notification-actions adds to VisitsController: that each sends the
/// right command shaped from the authenticated caller (never a client-supplied actor id)
/// and route id, and returns 204 on success.
/// </summary>
[Trait("Feature", "Bookings")]
public sealed class VisitsControllerTests
{
    [Fact(DisplayName = "ProposeAlternate sends the authenticated caller as OwnerId, with the route id and body")]
    public async Task ProposeAlternate_SendsCommand_AndReturnsNoContent()
    {
        var visitId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var proposedAt = DateTime.UtcNow.AddDays(3);

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(
                It.Is<ProposeAlternateVisitCommand>(c =>
                    c.VisitId == visitId && c.OwnerId == ownerId &&
                    c.ProposedAt == proposedAt && c.OwnerNote == "موعد بديل"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(sender.Object, ownerId);

        var response = await controller.ProposeAlternate(
            visitId, new ProposeAlternateRequest(proposedAt, "موعد بديل"), CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        sender.VerifyAll();
    }

    [Fact(DisplayName = "AcceptReschedule sends the authenticated caller as RequesterId, with the route id")]
    public async Task AcceptReschedule_SendsCommand_AndReturnsNoContent()
    {
        var visitId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(
                It.Is<AcceptRescheduledVisitCommand>(c => c.VisitId == visitId && c.RequesterId == requesterId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(sender.Object, requesterId);

        var response = await controller.AcceptReschedule(visitId, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        sender.VerifyAll();
    }

    [Fact(DisplayName = "DeclineReschedule sends the authenticated caller as RequesterId, with the route id")]
    public async Task DeclineReschedule_SendsCommand_AndReturnsNoContent()
    {
        var visitId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(
                It.Is<DeclineRescheduledVisitCommand>(c => c.VisitId == visitId && c.RequesterId == requesterId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(sender.Object, requesterId);

        var response = await controller.DeclineReschedule(visitId, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        sender.VerifyAll();
    }

    private static VisitsController CreateController(ISender sender, Guid userId)
    {
        var controller = new VisitsController(sender);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"))
        };

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
