using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PropertyApi.Application.Services.Commands.AcceptServiceRequest;
using PropertyApi.Application.Services.Commands.CreateServiceRequest;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Queries.GetServiceRequestById;
using PropertyApi.Controllers;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Integration.Tests.Controllers;

/// <summary>
/// Controller-level tests (mocked ISender, no HTTP host) — same style as
/// PropertyImagesControllerTests. A true WebApplicationFactory round-trip is not exercised
/// here: CreateServiceRequestCommandHandler depends on IServiceRequestNumberGenerator, which
/// runs a raw SQL `nextval()` against Postgres and has no EF Core InMemory-compatible path,
/// the same limitation the codebase's existing AgencyInvitation advisory-lock flow already has
/// (it also has zero WebApplicationFactory coverage for the same reason). Full HTTP-lifecycle
/// coverage against real Postgres is a documented follow-up, not something this test double
/// works around silently.
/// </summary>
[Trait("Feature", "ServiceRequests")]
public sealed class ServiceRequestsControllerTests
{
    [Fact(DisplayName = "Create sends the authenticated caller as RequesterId, never a client-supplied one")]
    public async Task Create_UsesAuthenticatedUserId_NotClientSupplied()
    {
        var propertyId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dto = new ServiceRequestDto(
            Guid.NewGuid(), "SR-2026-000001", propertyId, "Villa", null, userId, "Test User",
            Guid.NewGuid(), "AqarTech Verify", offeringId, "Standard Check",
            ServiceCategory.Verification, ServiceRequestStatus.UnderReview,
            null, null, null, null, null, null, null, null, null, null, DateTime.UtcNow);

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(It.IsAny<CreateServiceRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var controller = CreateController(sender.Object, userId);

        var response = await controller.Create(
            new CreateServiceRequestRequest(propertyId, offeringId, "الرجاء التحقق"), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(response);

        sender.Verify(x => x.Send(
            It.Is<CreateServiceRequestCommand>(c =>
                c.PropertyId == propertyId && c.RequesterId == userId && c.ServiceOfferingId == offeringId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "GetById passes IsAdmin=true only when the caller actually holds the Admin role")]
    public async Task GetById_NonAdminCaller_PassesIsAdminFalse()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dto = new ServiceRequestDto(
            requestId, "SR-2026-000002", Guid.NewGuid(), "Villa", null, userId, "Test User",
            Guid.NewGuid(), "AqarTech Verify", Guid.NewGuid(), "Standard Check",
            ServiceCategory.Verification, ServiceRequestStatus.UnderReview,
            null, null, null, null, null, null, null, null, null, null, DateTime.UtcNow);

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(It.IsAny<GetServiceRequestByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var controller = CreateController(sender.Object, userId, isAdmin: false);

        await controller.GetById(requestId, CancellationToken.None);

        sender.Verify(x => x.Send(
            It.Is<GetServiceRequestByIdQuery>(q =>
                q.ServiceRequestId == requestId && q.ActorUserId == userId && q.IsAdmin == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Accept delegates the authenticated caller as ActorUserId")]
    public async Task Accept_Delegates_ActorUserId()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dto = new ServiceRequestDto(
            requestId, "SR-2026-000003", Guid.NewGuid(), "Villa", null, Guid.NewGuid(), "Test User",
            Guid.NewGuid(), "AqarTech Verify", Guid.NewGuid(), "Standard Check",
            ServiceCategory.Verification, ServiceRequestStatus.Accepted,
            null, null, null, null, null, null, null, null, null, null, DateTime.UtcNow);

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(It.IsAny<AcceptServiceRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var controller = CreateController(sender.Object, userId);

        var response = await controller.Accept(
            requestId, new AcceptServiceRequestRequest(null, null, null), CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);

        sender.Verify(x => x.Send(
            It.Is<AcceptServiceRequestCommand>(c => c.ServiceRequestId == requestId && c.ActorUserId == userId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ServiceRequestsController CreateController(ISender sender, Guid userId, bool isAdmin = false)
    {
        var controller = new ServiceRequestsController(sender);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };

        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"))
        };

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
