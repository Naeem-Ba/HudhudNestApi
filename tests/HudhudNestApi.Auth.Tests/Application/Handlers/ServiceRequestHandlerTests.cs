using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Services.Commands.AcceptServiceRequest;
using HudhudNestApi.Application.Services.Commands.AddServiceReview;
using HudhudNestApi.Application.Services.Commands.CancelServiceRequest;
using HudhudNestApi.Application.Services.Commands.CreateServiceRequest;
using HudhudNestApi.Application.Services.Commands.RejectServiceRequest;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Auth.Tests.Application.Handlers;

[Trait("Category", "Application")]
[Trait("Feature", "ServiceRequests")]
public sealed class ServiceRequestHandlerTests
{
    private static readonly DateTime UtcNow = DateTime.UtcNow;

    private static ServiceProvider MakeProvider(Guid? userId = null) =>
        ServiceProvider.Create(
            userId ?? Guid.NewGuid(), null, "HudhudNest Verify", null, null, null, UtcNow);

    private static ServiceOffering MakeOffering(Guid providerId, bool isActive = true)
    {
        var offering = ServiceOffering.Create(
            providerId, ServiceCategory.Verification, "Standard Check", null, null, null, null, UtcNow);
        if (!isActive) offering.Deactivate(UtcNow);
        return offering;
    }

    private static ServiceRequest MakeRequest(Guid propertyId, Guid requesterId, Guid providerId, Guid offeringId) =>
        ServiceRequest.Create(
            "SR-2026-000001", propertyId, requesterId, providerId, offeringId,
            ServiceCategory.Verification, null, UtcNow);

    // ── CreateServiceRequest ──────────────────────────────────────────

    [Fact(DisplayName = "CreateServiceRequest: happy path creates a request at UnderReview")]
    public async Task Create_ValidInputs_CreatesRequest()
    {
        var propertyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id);

        var properties = new Mock<IPropertyReadRepository>();
        properties.Setup(x => x.GetByIdAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertySummary(propertyId, "Villa", "Damascus", Guid.NewGuid(), true, null));

        var offerings = new Mock<IServiceOfferingRepository>();
        offerings.Setup(x => x.GetByIdAsync(offering.Id, It.IsAny<CancellationToken>())).ReturnsAsync(offering);

        var providers = new Mock<IServiceProviderRepository>();
        providers.Setup(x => x.GetByIdAsync(provider.Id, It.IsAny<CancellationToken>())).ReturnsAsync(provider);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(x => x.HasActiveRequestAsync(propertyId, requesterId, offering.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        requests.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceRequest?)null); // forces the handler's ?? fallback to the in-memory entity

        var history = new Mock<IServiceRequestStatusHistoryRepository>();
        var numbers = new Mock<IServiceRequestNumberGenerator>();
        numbers.Setup(x => x.NextAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SR-2026-000001");

        var handler = new CreateServiceRequestCommandHandler(
            properties.Object, offerings.Object, providers.Object, requests.Object, history.Object,
            numbers.Object, Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<CreateServiceRequestCommandHandler>>());

        var result = await handler.Handle(
            new CreateServiceRequestCommand(propertyId, requesterId, offering.Id, "الرجاء التحقق"),
            CancellationToken.None);

        Assert.Equal("SR-2026-000001", result.RequestNumber);
        Assert.Equal(ServiceRequestStatus.UnderReview, result.Status);
        requests.Verify(x => x.AddAsync(It.IsAny<ServiceRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        history.Verify(x => x.AddAsync(It.IsAny<ServiceRequestStatusHistory>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "CreateServiceRequest: inactive offering throws ConflictException")]
    public async Task Create_InactiveOffering_ThrowsConflict()
    {
        var propertyId = Guid.NewGuid();
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id, isActive: false);

        var properties = new Mock<IPropertyReadRepository>();
        properties.Setup(x => x.GetByIdAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertySummary(propertyId, "Villa", "Damascus", Guid.NewGuid(), true, null));

        var offerings = new Mock<IServiceOfferingRepository>();
        offerings.Setup(x => x.GetByIdAsync(offering.Id, It.IsAny<CancellationToken>())).ReturnsAsync(offering);

        var handler = new CreateServiceRequestCommandHandler(
            properties.Object, offerings.Object, Mock.Of<IServiceProviderRepository>(),
            Mock.Of<IServiceRequestRepository>(), Mock.Of<IServiceRequestStatusHistoryRepository>(),
            Mock.Of<IServiceRequestNumberGenerator>(), Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<CreateServiceRequestCommandHandler>>());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateServiceRequestCommand(propertyId, Guid.NewGuid(), offering.Id, null),
            CancellationToken.None));
    }

    [Fact(DisplayName = "CreateServiceRequest: duplicate active request throws ConflictException")]
    public async Task Create_DuplicateActiveRequest_ThrowsConflict()
    {
        var propertyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id);

        var properties = new Mock<IPropertyReadRepository>();
        properties.Setup(x => x.GetByIdAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertySummary(propertyId, "Villa", "Damascus", Guid.NewGuid(), true, null));

        var offerings = new Mock<IServiceOfferingRepository>();
        offerings.Setup(x => x.GetByIdAsync(offering.Id, It.IsAny<CancellationToken>())).ReturnsAsync(offering);

        var providers = new Mock<IServiceProviderRepository>();
        providers.Setup(x => x.GetByIdAsync(provider.Id, It.IsAny<CancellationToken>())).ReturnsAsync(provider);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(x => x.HasActiveRequestAsync(propertyId, requesterId, offering.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new CreateServiceRequestCommandHandler(
            properties.Object, offerings.Object, providers.Object, requests.Object,
            Mock.Of<IServiceRequestStatusHistoryRepository>(), Mock.Of<IServiceRequestNumberGenerator>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<CreateServiceRequestCommandHandler>>());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateServiceRequestCommand(propertyId, requesterId, offering.Id, null),
            CancellationToken.None));
    }

    // ── AcceptServiceRequest ──────────────────────────────────────────

    [Fact(DisplayName = "AcceptServiceRequest: an actor who is not the owning provider is forbidden")]
    public async Task Accept_WrongActor_ThrowsForbidden()
    {
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id);
        var serviceRequest = MakeRequest(Guid.NewGuid(), Guid.NewGuid(), provider.Id, offering.Id);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(x => x.GetByIdAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceRequest);

        var providers = new Mock<IServiceProviderRepository>();
        providers.Setup(x => x.GetByIdAsync(provider.Id, It.IsAny<CancellationToken>())).ReturnsAsync(provider);

        var handler = new AcceptServiceRequestCommandHandler(
            requests.Object, providers.Object, Mock.Of<IServiceRequestStatusHistoryRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<AcceptServiceRequestCommandHandler>>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new AcceptServiceRequestCommand(serviceRequest.Id, Guid.NewGuid(), null, null, null),
            CancellationToken.None));
    }

    [Fact(DisplayName = "AcceptServiceRequest: the owning provider succeeds")]
    public async Task Accept_OwningProvider_Succeeds()
    {
        var providerUserId = Guid.NewGuid();
        var provider = MakeProvider(providerUserId);
        var offering = MakeOffering(provider.Id);
        var serviceRequest = MakeRequest(Guid.NewGuid(), Guid.NewGuid(), provider.Id, offering.Id);

        var requests = new Mock<IServiceRequestRepository>();
        requests.SetupSequence(x => x.GetByIdAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceRequest)
            .ReturnsAsync(serviceRequest);

        var providers = new Mock<IServiceProviderRepository>();
        providers.Setup(x => x.GetByIdAsync(provider.Id, It.IsAny<CancellationToken>())).ReturnsAsync(provider);

        var handler = new AcceptServiceRequestCommandHandler(
            requests.Object, providers.Object, Mock.Of<IServiceRequestStatusHistoryRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<AcceptServiceRequestCommandHandler>>());

        var result = await handler.Handle(
            new AcceptServiceRequestCommand(serviceRequest.Id, providerUserId, 100m, 1, "سنبدأ قريباً"),
            CancellationToken.None);

        Assert.Equal(ServiceRequestStatus.Accepted, result.Status);
    }

    // ── RejectServiceRequest ──────────────────────────────────────────

    [Fact(DisplayName = "RejectServiceRequest: an already-accepted request throws 409 InvalidStateTransitionException")]
    public async Task Reject_AlreadyAccepted_ThrowsInvalidStateTransition()
    {
        var providerUserId = Guid.NewGuid();
        var provider = MakeProvider(providerUserId);
        var offering = MakeOffering(provider.Id);
        var serviceRequest = MakeRequest(Guid.NewGuid(), Guid.NewGuid(), provider.Id, offering.Id);
        serviceRequest.Accept(null, null, null, UtcNow);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(x => x.GetByIdAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceRequest);

        var providers = new Mock<IServiceProviderRepository>();
        providers.Setup(x => x.GetByIdAsync(provider.Id, It.IsAny<CancellationToken>())).ReturnsAsync(provider);

        var handler = new RejectServiceRequestCommandHandler(
            requests.Object, providers.Object, Mock.Of<IServiceRequestStatusHistoryRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<RejectServiceRequestCommandHandler>>());

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() => handler.Handle(
            new RejectServiceRequestCommand(serviceRequest.Id, providerUserId, "غير متاح"),
            CancellationToken.None));
    }

    // ── CancelServiceRequest ──────────────────────────────────────────

    [Fact(DisplayName = "CancelServiceRequest: an unrelated actor is forbidden")]
    public async Task Cancel_UnrelatedActor_ThrowsForbidden()
    {
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id);
        var serviceRequest = MakeRequest(Guid.NewGuid(), Guid.NewGuid(), provider.Id, offering.Id);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(x => x.GetByIdAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceRequest);

        var providers = new Mock<IServiceProviderRepository>();
        providers.Setup(x => x.GetByIdAsync(provider.Id, It.IsAny<CancellationToken>())).ReturnsAsync(provider);

        var handler = new CancelServiceRequestCommandHandler(
            requests.Object, providers.Object, Mock.Of<IServiceRequestStatusHistoryRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<CancelServiceRequestCommandHandler>>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new CancelServiceRequestCommand(serviceRequest.Id, Guid.NewGuid(), IsAdmin: false, Reason: null),
            CancellationToken.None));
    }

    [Fact(DisplayName = "CancelServiceRequest: the requester can cancel their own request")]
    public async Task Cancel_Requester_Succeeds()
    {
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id);
        var requesterId = Guid.NewGuid();
        var serviceRequest = MakeRequest(Guid.NewGuid(), requesterId, provider.Id, offering.Id);

        var requests = new Mock<IServiceRequestRepository>();
        requests.SetupSequence(x => x.GetByIdAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceRequest)
            .ReturnsAsync(serviceRequest);

        var providers = new Mock<IServiceProviderRepository>();
        providers.Setup(x => x.GetByIdAsync(provider.Id, It.IsAny<CancellationToken>())).ReturnsAsync(provider);

        var handler = new CancelServiceRequestCommandHandler(
            requests.Object, providers.Object, Mock.Of<IServiceRequestStatusHistoryRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<CancelServiceRequestCommandHandler>>());

        var result = await handler.Handle(
            new CancelServiceRequestCommand(serviceRequest.Id, requesterId, IsAdmin: false, Reason: "تغيّرت الخطط"),
            CancellationToken.None);

        Assert.Equal(ServiceRequestStatus.Cancelled, result.Status);
    }

    // ── AddServiceReview ───────────────────────────────────────────────

    [Fact(DisplayName = "AddServiceReview: a reviewer who is not the requester is forbidden")]
    public async Task AddReview_WrongReviewer_ThrowsForbidden()
    {
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id);
        var requesterId = Guid.NewGuid();
        var serviceRequest = MakeRequest(Guid.NewGuid(), requesterId, provider.Id, offering.Id);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(x => x.GetByIdAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceRequest);

        var handler = new AddServiceReviewCommandHandler(
            requests.Object, Mock.Of<IServiceReviewRepository>(), Mock.Of<IUserAccountRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<AddServiceReviewCommandHandler>>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new AddServiceReviewCommand(serviceRequest.Id, Guid.NewGuid(), 5, "ممتاز"),
            CancellationToken.None));
    }

    [Fact(DisplayName = "AddServiceReview: a request that is not yet Completed throws DomainException")]
    public async Task AddReview_NotCompleted_ThrowsDomainException()
    {
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id);
        var requesterId = Guid.NewGuid();
        var serviceRequest = MakeRequest(Guid.NewGuid(), requesterId, provider.Id, offering.Id);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(x => x.GetByIdAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceRequest);

        var handler = new AddServiceReviewCommandHandler(
            requests.Object, Mock.Of<IServiceReviewRepository>(), Mock.Of<IUserAccountRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<AddServiceReviewCommandHandler>>());

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new AddServiceReviewCommand(serviceRequest.Id, requesterId, 5, null),
            CancellationToken.None));
    }

    [Fact(DisplayName = "AddServiceReview: happy path marks the request Reviewed")]
    public async Task AddReview_Completed_Succeeds_AndMarksReviewed()
    {
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id);
        var requesterId = Guid.NewGuid();
        var serviceRequest = MakeRequest(Guid.NewGuid(), requesterId, provider.Id, offering.Id);
        serviceRequest.Accept(null, null, null, UtcNow);
        serviceRequest.Schedule(UtcNow.AddDays(1), UtcNow);
        serviceRequest.Start(UtcNow);
        serviceRequest.Complete(null, null, UtcNow);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(x => x.GetByIdAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceRequest);

        var reviews = new Mock<IServiceReviewRepository>();
        reviews.Setup(x => x.ExistsForRequestAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new AddServiceReviewCommandHandler(
            requests.Object, reviews.Object, Mock.Of<IUserAccountRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<AddServiceReviewCommandHandler>>());

        var result = await handler.Handle(
            new AddServiceReviewCommand(serviceRequest.Id, requesterId, 5, "ممتاز"),
            CancellationToken.None);

        Assert.Equal(5, result.Rating);
        Assert.Equal(ServiceRequestStatus.Reviewed, serviceRequest.Status);
        reviews.Verify(x => x.AddAsync(It.IsAny<ServiceReview>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "AddServiceReview: an already-reviewed request throws DomainException")]
    public async Task AddReview_AlreadyReviewed_ThrowsDomainException()
    {
        var provider = MakeProvider();
        var offering = MakeOffering(provider.Id);
        var requesterId = Guid.NewGuid();
        var serviceRequest = MakeRequest(Guid.NewGuid(), requesterId, provider.Id, offering.Id);
        serviceRequest.Accept(null, null, null, UtcNow);
        serviceRequest.Schedule(UtcNow.AddDays(1), UtcNow);
        serviceRequest.Start(UtcNow);
        serviceRequest.Complete(null, null, UtcNow);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(x => x.GetByIdAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceRequest);

        var reviews = new Mock<IServiceReviewRepository>();
        reviews.Setup(x => x.ExistsForRequestAsync(serviceRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new AddServiceReviewCommandHandler(
            requests.Object, reviews.Object, Mock.Of<IUserAccountRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<ILogger<AddServiceReviewCommandHandler>>());

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new AddServiceReviewCommand(serviceRequest.Id, requesterId, 4, null),
            CancellationToken.None));
    }
}
