using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Controllers;

namespace HudhudNestApi.Integration.Tests.Controllers;

/// <summary>
/// Controller-level tests (mocked IAdminService/IAdminSubscriptionService/IAdminListingService,
/// no HTTP host) — same style as ServiceRequestsControllerTests. AdminController does its own
/// actor-resolution (GetCurrentUserId) and status mapping (ToActionResult); both are exercised
/// here without needing a database, matching the pattern already used for controllers whose
/// underlying handlers depend on Postgres-only features.
/// </summary>
[Trait("Feature", "Admin")]
public sealed class AdminControllerTests
{
    [Fact(DisplayName = "GetUsers returns a paged envelope built from the service result")]
    public async Task GetUsers_ReturnsPagedEnvelope()
    {
        var admin = new Mock<IAdminService>();
        var page = new PagedResult<AdminUserDto>
        {
            Items = new List<AdminUserDto> { new() { Id = Guid.NewGuid(), FirstName = "A" } },
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };
        admin.Setup(x => x.GetUsersWithPaginationAsync(1, 20, null, "a", "free", "Active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var controller = CreateController(admin.Object);

        var response = await controller.GetUsers(1, 20, null, "a", "free", "Active", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.NotNull(ok.Value);
    }

    [Fact(DisplayName = "GetUserDetail returns 404 when the service finds no user")]
    public async Task GetUserDetail_Missing_ReturnsNotFound()
    {
        var admin = new Mock<IAdminService>();
        admin.Setup(x => x.GetUserDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminUserDetailDto?)null);

        var controller = CreateController(admin.Object);

        var response = await controller.GetUserDetail(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(response);
    }

    [Fact(DisplayName = "GetUserDetail returns 200 with the detail DTO when found")]
    public async Task GetUserDetail_Found_ReturnsOk()
    {
        var admin = new Mock<IAdminService>();
        var detail = new AdminUserDetailDto { Id = Guid.NewGuid(), PlanStatus = "Active" };
        admin.Setup(x => x.GetUserDetailAsync(detail.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var controller = CreateController(admin.Object);

        var response = await controller.GetUserDetail(detail.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Same(detail, ok.Value);
    }

    [Fact(DisplayName = "GetUserProperties returns a paged envelope from the listing service")]
    public async Task GetUserProperties_ReturnsPagedEnvelope()
    {
        var listings = new Mock<IAdminListingService>();
        var userId = Guid.NewGuid();
        var page = new PagedResult<AdminPropertySummaryDto>
        {
            Items = new List<AdminPropertySummaryDto> { new() { Id = Guid.NewGuid(), Title = "X" } },
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };
        listings.Setup(x => x.GetUserPropertiesAsync(userId, 1, 20, "Active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var controller = CreateController(listings: listings.Object);

        var response = await controller.GetUserProperties(userId, 1, 20, "Active", CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
    }

    [Fact(DisplayName = "ActivateSubscription returns 401 when the caller cannot be resolved")]
    public async Task ActivateSubscription_NoActor_ReturnsUnauthorized()
    {
        var controller = CreateController(withActor: false);

        var response = await controller.ActivateSubscription(
            Guid.NewGuid(), new ActivateSubscriptionRequest("premium", 30, "test"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact(DisplayName = "ActivateSubscription delegates to the subscription service with the resolved actor")]
    public async Task ActivateSubscription_Delegates_ToSubscriptionService()
    {
        var subscriptions = new Mock<IAdminSubscriptionService>();
        var userId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        subscriptions
            .Setup(x => x.ActivatePlanAsync(userId, "premium", 30, "reason", actorId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("Plan activated."));

        var controller = CreateController(subscriptions: subscriptions.Object, actorId: actorId);

        var response = await controller.ActivateSubscription(
            userId, new ActivateSubscriptionRequest("premium", 30, "reason"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        subscriptions.VerifyAll();
    }

    [Fact(DisplayName = "ExtendSubscription maps a conflict result to 409")]
    public async Task ExtendSubscription_Conflict_ReturnsConflict()
    {
        var subscriptions = new Mock<IAdminSubscriptionService>();
        subscriptions
            .Setup(x => x.ExtendSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.ConflictResult("Cannot extend a plan that was never selected."));

        var controller = CreateController(subscriptions: subscriptions.Object);

        var response = await controller.ExtendSubscription(
            Guid.NewGuid(), new ExtendSubscriptionRequest(30, null), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(response);
    }

    [Fact(DisplayName = "ExtendSubscription returns 401 when the caller cannot be resolved")]
    public async Task ExtendSubscription_NoActor_ReturnsUnauthorized()
    {
        var controller = CreateController(withActor: false);

        var response = await controller.ExtendSubscription(Guid.NewGuid(), new ExtendSubscriptionRequest(30, null), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact(DisplayName = "CancelSubscription maps a not-found result to 404")]
    public async Task CancelSubscription_UserNotFound_ReturnsNotFound()
    {
        var subscriptions = new Mock<IAdminSubscriptionService>();
        subscriptions
            .Setup(x => x.CancelSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.UserNotFound());

        var controller = CreateController(subscriptions: subscriptions.Object);

        var response = await controller.CancelSubscription(Guid.NewGuid(), new CancelSubscriptionRequest(null), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(response);
    }

    [Fact(DisplayName = "CancelSubscription returns 401 when the caller cannot be resolved")]
    public async Task CancelSubscription_NoActor_ReturnsUnauthorized()
    {
        var controller = CreateController(withActor: false);

        var response = await controller.CancelSubscription(Guid.NewGuid(), new CancelSubscriptionRequest(null), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact(DisplayName = "FeatureListing defaults Days to 30 when the caller omits it")]
    public async Task FeatureListing_NoDays_DefaultsTo30()
    {
        var listings = new Mock<IAdminListingService>();
        var propertyId = Guid.NewGuid();
        listings
            .Setup(x => x.FeatureListingAsync(propertyId, 30, "reason", It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("Featured."));

        var controller = CreateController(listings: listings.Object);

        var response = await controller.FeatureListing(propertyId, new FeatureListingRequest(null, "reason"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        listings.VerifyAll();
    }

    [Fact(DisplayName = "FeatureListing returns 401 when the caller cannot be resolved")]
    public async Task FeatureListing_NoActor_ReturnsUnauthorized()
    {
        var controller = CreateController(withActor: false);

        var response = await controller.FeatureListing(Guid.NewGuid(), new FeatureListingRequest(14, null), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact(DisplayName = "UnfeatureListing delegates to the listing service")]
    public async Task UnfeatureListing_Delegates()
    {
        var listings = new Mock<IAdminListingService>();
        var propertyId = Guid.NewGuid();
        listings
            .Setup(x => x.UnfeatureListingAsync(propertyId, "no longer relevant", It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("Unfeatured."));

        var controller = CreateController(listings: listings.Object);

        var response = await controller.UnfeatureListing(propertyId, new UnfeatureListingRequest("no longer relevant"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
    }

    [Fact(DisplayName = "UnfeatureListing returns 401 when the caller cannot be resolved")]
    public async Task UnfeatureListing_NoActor_ReturnsUnauthorized()
    {
        var controller = CreateController(withActor: false);

        var response = await controller.UnfeatureListing(Guid.NewGuid(), new UnfeatureListingRequest(null), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact(DisplayName = "ExtendListing succeeds for a free-plan owner's listing — no quota gate in the controller")]
    public async Task ExtendListing_Delegates_RegardlessOfOwnerPlan()
    {
        var listings = new Mock<IAdminListingService>();
        var propertyId = Guid.NewGuid();
        listings
            .Setup(x => x.ExtendListingAsync(propertyId, 14, "goodwill extension", It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("Extended."));

        var controller = CreateController(listings: listings.Object);

        var response = await controller.ExtendListing(propertyId, new ExtendListingRequest(14, "goodwill extension"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
    }

    [Fact(DisplayName = "ExtendListing returns 401 when the caller cannot be resolved")]
    public async Task ExtendListing_NoActor_ReturnsUnauthorized()
    {
        var controller = CreateController(withActor: false);

        var response = await controller.ExtendListing(Guid.NewGuid(), new ExtendListingRequest(14, null), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact(DisplayName = "GetRoles returns the service's role list")]
    public void GetRoles_ReturnsServiceRoles()
    {
        var admin = new Mock<IAdminService>();
        admin.Setup(x => x.GetRoles()).Returns(new[] { "User", "Admin" });

        var controller = CreateController(admin.Object);

        var response = controller.GetRoles();

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(new[] { "User", "Admin" }, ok.Value);
    }

    [Fact(DisplayName = "UpdateRole maps an invalid-role result to 400 with the allowed roles")]
    public async Task UpdateRole_InvalidRole_ReturnsBadRequestWithAllowedRoles()
    {
        var admin = new Mock<IAdminService>();
        admin.Setup(x => x.SetUserRoleAsync(It.IsAny<Guid>(), "Nope", It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.InvalidRole(new[] { "User", "Admin" }));

        var controller = CreateController(admin.Object);

        var response = await controller.UpdateRole(Guid.NewGuid(), new UpdateRoleRequest("Nope"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response);
    }

    [Fact(DisplayName = "UpdateRole returns 401 when the caller cannot be resolved")]
    public async Task UpdateRole_NoActor_ReturnsUnauthorized()
    {
        var controller = CreateController(withActor: false);

        var response = await controller.UpdateRole(Guid.NewGuid(), new UpdateRoleRequest("Admin"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact(DisplayName = "AssignRole delegates to the admin service")]
    public async Task AssignRole_Delegates()
    {
        var admin = new Mock<IAdminService>();
        var userId = Guid.NewGuid();
        admin.Setup(x => x.AssignRoleAsync(userId, "Agent", It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("Role assigned."));

        var controller = CreateController(admin.Object);

        var response = await controller.AssignRole(userId, "Agent", CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
    }

    [Fact(DisplayName = "AssignRole returns 401 when the caller cannot be resolved")]
    public async Task AssignRole_NoActor_ReturnsUnauthorized()
    {
        var controller = CreateController(withActor: false);

        var response = await controller.AssignRole(Guid.NewGuid(), "Agent", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact(DisplayName = "RemoveRole delegates to the admin service")]
    public async Task RemoveRole_Delegates()
    {
        var admin = new Mock<IAdminService>();
        var userId = Guid.NewGuid();
        admin.Setup(x => x.RemoveRoleAsync(userId, "Agent", It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("Role removed."));

        var controller = CreateController(admin.Object);

        var response = await controller.RemoveRole(userId, "Agent", CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
    }

    [Fact(DisplayName = "RemoveRole returns 401 when the caller cannot be resolved")]
    public async Task RemoveRole_NoActor_ReturnsUnauthorized()
    {
        var controller = CreateController(withActor: false);

        var response = await controller.RemoveRole(Guid.NewGuid(), "Agent", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    [Fact(DisplayName = "DisableUser returns 404 when the service finds no user")]
    public async Task DisableUser_Missing_ReturnsNotFound()
    {
        var admin = new Mock<IAdminService>();
        admin.Setup(x => x.DisableUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.UserNotFound());

        var controller = CreateController(admin.Object);

        var response = await controller.DisableUser(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(response);
    }

    [Fact(DisplayName = "DisableUser returns 400 when the service reports failure without not-found")]
    public async Task DisableUser_Failed_ReturnsBadRequest()
    {
        var admin = new Mock<IAdminService>();
        admin.Setup(x => x.DisableUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.BadRequest("Cannot disable the last admin."));

        var controller = CreateController(admin.Object);

        var response = await controller.DisableUser(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response);
    }

    [Fact(DisplayName = "DisableUser returns 204 when the service succeeds")]
    public async Task DisableUser_Succeeded_ReturnsNoContent()
    {
        var admin = new Mock<IAdminService>();
        admin.Setup(x => x.DisableUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("Disabled."));

        var controller = CreateController(admin.Object);

        var response = await controller.DisableUser(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
    }

    private static AdminController CreateController(
        IAdminService? admin = null,
        IAdminSubscriptionService? subscriptions = null,
        IAdminListingService? listings = null,
        IAdminValuationInquiryService? valuation = null,
        bool withActor = true,
        Guid? actorId = null)
    {
        var controller = new AdminController(
            admin ?? Mock.Of<IAdminService>(),
            subscriptions ?? Mock.Of<IAdminSubscriptionService>(),
            listings ?? Mock.Of<IAdminListingService>(),
            valuation ?? Mock.Of<IAdminValuationInquiryService>());

        var claims = new List<Claim>();
        if (withActor)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, (actorId ?? Guid.NewGuid()).ToString()));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"))
        };

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
