using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Admin.Services;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Tests.Admin;

public sealed class AdminListingServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    /// <summary>
    /// The concrete proof of the explicit business rule: admin extension must succeed for
    /// a listing whose owner is on the free plan (or any plan at all) — extend never
    /// consults plan/quota, only whether the listing itself exists and isn't deleted.
    /// </summary>
    [Fact]
    public async Task ExtendListingAsync_SucceedsRegardlessOfOwnerPlan_NoQuotaCheckInvolved()
    {
        var property = Property.Create("شقة للإيجار", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var oldExpiry = property.ExpiresAt;

        var properties = FakeProperties(property);
        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), auditLogs.Object);

        var result = await service.ExtendListingAsync(
            property.Id, 90, "free-tier owner, admin override", AdminId, "127.0.0.1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotEqual(oldExpiry, property.ExpiresAt);
        Assert.NotNull(property.ExpiresAt);
        auditLogs.Verify(x => x.LogAsync(
            AdminId, AuditActions.ListingExtendedByAdmin, "127.0.0.1",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtendListingAsync_PublishesPropertyPublishedEvent_SoDistributionCanReRun()
    {
        var property = Property.Create("شقة للإيجار", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var properties = FakeProperties(property);
        var publisher = new Mock<IPublisher>();
        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>(), publisher.Object);

        await service.ExtendListingAsync(property.Id, 90, null, AdminId, null, CancellationToken.None);

        publisher.Verify(
            x => x.Publish(It.Is<PropertyPublishedEvent>(e => e.PropertyId == property.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FeatureListingAsync_OnAvailableListing_MarksFeatured_AndAuditLogs()
    {
        var property = Property.Create("فيلا للبيع", "وصف", Guid.NewGuid(), ListingType.ForSale);

        var properties = FakeProperties(property);
        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), auditLogs.Object);

        var result = await service.FeatureListingAsync(
            property.Id, 30, null, AdminId, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(property.IsFeatured);
        Assert.NotNull(property.FeaturedUntil);
    }

    /// <summary>
    /// The admin dashboard does not bypass Property's own domain guard: an Expired listing
    /// still cannot be featured directly (must be extended first) — same rule
    /// ConfirmFeaturedListingPaymentCommandHandler already relies on.
    /// </summary>
    [Fact]
    public async Task FeatureListingAsync_OnExpiredListing_Throws_DomainRuleNotBypassed()
    {
        var property = Property.Create("أرض للبيع", "وصف", Guid.NewGuid(), ListingType.ForSale);
        property.ChangeStatus(PropertyStatus.Expired);

        var properties = FakeProperties(property);
        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        await Assert.ThrowsAsync<DomainException>(() =>
            service.FeatureListingAsync(property.Id, 30, null, AdminId, null, CancellationToken.None));
    }

    [Fact]
    public async Task UnfeatureListingAsync_ClearsFeaturedFlag()
    {
        var property = Property.Create("منزل للإيجار", "وصف", Guid.NewGuid(), ListingType.ForRent);
        property.MarkFeatured(TimeSpan.FromDays(30), DateTime.UtcNow);

        var properties = FakeProperties(property);
        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.UnfeatureListingAsync(
            property.Id, "requested by owner", AdminId, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(property.IsFeatured);
    }

    [Fact]
    public async Task UnfeatureListingAsync_WhenListingMissing_ReturnsNotFound()
    {
        var propertyId = Guid.NewGuid();
        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByIdAsync(propertyId, It.IsAny<CancellationToken>())).ReturnsAsync((Property?)null);

        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.UnfeatureListingAsync(propertyId, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task UnfeatureListingAsync_WhenSaveFails_RollsBackAndRethrows()
    {
        var property = Property.Create("منزل للإيجار", "وصف", Guid.NewGuid(), ListingType.ForRent);
        property.MarkFeatured(TimeSpan.FromDays(30), DateTime.UtcNow);

        var properties = FakeProperties(property);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var service = BuildService(properties.Object, unitOfWork.Object, Mock.Of<IAuditLogService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UnfeatureListingAsync(property.Id, null, AdminId, null, CancellationToken.None));

        unitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FeatureListingAsync_WhenListingMissing_ReturnsNotFound()
    {
        var propertyId = Guid.NewGuid();
        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByIdAsync(propertyId, It.IsAny<CancellationToken>())).ReturnsAsync((Property?)null);

        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.FeatureListingAsync(propertyId, 30, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(731)]
    public async Task FeatureListingAsync_WithOutOfRangeDuration_ReturnsBadRequest(int days)
    {
        var property = Property.Create("شقة", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var properties = FakeProperties(property);
        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.FeatureListingAsync(property.Id, days, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ExtendListingAsync_WhenListingMissing_ReturnsNotFound()
    {
        var propertyId = Guid.NewGuid();
        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByIdAsync(propertyId, It.IsAny<CancellationToken>())).ReturnsAsync((Property?)null);

        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.ExtendListingAsync(propertyId, 30, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(731)]
    public async Task ExtendListingAsync_WithOutOfRangeDuration_ReturnsBadRequest(int days)
    {
        var property = Property.Create("شقة", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var properties = FakeProperties(property);
        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.ExtendListingAsync(property.Id, days, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ExtendListingAsync_WhenSaveFails_RollsBackAndRethrows()
    {
        var property = Property.Create("شقة للإيجار", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var properties = FakeProperties(property);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var service = BuildService(properties.Object, unitOfWork.Object, Mock.Of<IAuditLogService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExtendListingAsync(property.Id, 30, null, AdminId, null, CancellationToken.None));

        unitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetUserPropertiesAsync_FiltersByStatus_OrdersByNewest_AndPaginates()
    {
        var ownerId = Guid.NewGuid();
        var older = Property.Create("قديم", "وصف", ownerId, ListingType.ForRent);
        var newer = Property.Create("جديد", "وصف", ownerId, ListingType.ForSale);
        newer.MarkFeatured(TimeSpan.FromDays(10), DateTime.UtcNow);
        newer.CreatedAt = older.CreatedAt.AddMinutes(5);

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Property> { older, newer });

        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.GetUserPropertiesAsync(ownerId, page: 1, pageSize: 10, status: null, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(newer.Id, result.Items[0].Id);
        Assert.True(result.Items[0].IsFeatured);
        Assert.Equal(older.Id, result.Items[1].Id);
    }

    [Fact]
    public async Task GetUserPropertiesAsync_WithStatusFilter_ReturnsOnlyMatchingStatus()
    {
        var ownerId = Guid.NewGuid();
        var draft = Property.Create("مسودة", "وصف", ownerId, ListingType.ForRent);
        var expired = Property.Create("منتهي", "وصف", ownerId, ListingType.ForSale);
        expired.ChangeStatus(PropertyStatus.Expired);

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Property> { draft, expired });

        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.GetUserPropertiesAsync(ownerId, page: 1, pageSize: 10, status: "Expired", CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(expired.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task GetUserPropertiesAsync_ClampsPageAndPageSize()
    {
        var ownerId = Guid.NewGuid();
        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Property>());

        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.GetUserPropertiesAsync(ownerId, page: 0, pageSize: 1000, status: null, CancellationToken.None);

        Assert.Equal(1, result.Page);
        Assert.Equal(100, result.PageSize);
    }

    private static AdminListingService BuildService(
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogs,
        IPublisher? publisher = null,
        INotificationService? notifications = null)
        => new(
            properties,
            unitOfWork,
            auditLogs,
            publisher ?? Mock.Of<IPublisher>(),
            notifications ?? Mock.Of<INotificationService>(),
            NullLogger<AdminListingService>.Instance);

    [Fact(DisplayName = "P8-D02: an admin cannot extend a sold listing back onto the market")]
    public async Task ExtendListingAsync_OnASoldListing_IsRefused()
    {
        var property = Property.Create("شقة", "وصف", Guid.NewGuid(), ListingType.ForSale);
        property.ChangeStatus(PropertyStatus.Sold);
        var service = BuildService(FakeProperties(property).Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            service.ExtendListingAsync(property.Id, 30, null, AdminId, null, CancellationToken.None));

        Assert.Equal(PropertyStatus.Sold, property.Status);
    }

    [Fact(DisplayName = "P8-D10: a take-down hides the listing, is audited, and tells the owner the reason")]
    public async Task TakeDownListingAsync_UnpublishesAuditsAndNotifiesTheOwner()
    {
        var property = Property.Create("شقة", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var auditLogs = new Mock<IAuditLogService>();
        var notifications = new Mock<INotificationService>();
        var service = BuildService(FakeProperties(property).Object, Mock.Of<IUnitOfWork>(), auditLogs.Object, notifications: notifications.Object);

        var result = await service.TakeDownListingAsync(property.Id, "  صور غير لائقة  ", AdminId, "10.0.0.1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(property.IsTakenDown);
        Assert.False(property.IsPublished);
        Assert.Equal("صور غير لائقة", property.TakenDownReason);
        auditLogs.Verify(x => x.LogAsync(
            AdminId, AuditActions.ListingTakenDownByAdmin, "10.0.0.1",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(x => x.NotifyPropertyUpdateAsync(
            property.OwnerId, property.Id, property.Title, NotificationType.ListingTakenDown,
            It.Is<NotificationText>(t => t.Literal == "صور غير لائقة"), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TakeDownListingAsync_WithoutAReason_IsABadRequestAndChangesNothing()
    {
        var property = Property.Create("شقة", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var service = BuildService(FakeProperties(property).Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.TakeDownListingAsync(property.Id, "   ", AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.NotFound);
        Assert.False(result.Conflict);
        Assert.False(property.IsTakenDown);
        Assert.True(property.IsPublished);
    }

    [Fact]
    public async Task TakeDownListingAsync_Twice_IsAConflict()
    {
        var property = Property.Create("شقة", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var service = BuildService(FakeProperties(property).Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());
        await service.TakeDownListingAsync(property.Id, "reason", AdminId, null, CancellationToken.None);

        var second = await service.TakeDownListingAsync(property.Id, "reason", AdminId, null, CancellationToken.None);

        Assert.True(second.Conflict);
    }

    [Fact]
    public async Task TakeDownListingAsync_ForAnUnknownListing_IsNotFound()
    {
        var properties = new Mock<IPropertyRepository>();
        var service = BuildService(properties.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.TakeDownListingAsync(Guid.NewGuid(), "reason", AdminId, null, CancellationToken.None);

        Assert.True(result.NotFound);
    }

    [Fact(DisplayName = "P8-D10: a notification failure never undoes a committed take-down")]
    public async Task TakeDownListingAsync_WhenTheNotificationFails_StillSucceeds()
    {
        var property = Property.Create("شقة", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(x => x.NotifyPropertyUpdateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("push down"));
        var service = BuildService(FakeProperties(property).Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>(), notifications: notifications.Object);

        var result = await service.TakeDownListingAsync(property.Id, "reason", AdminId, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(property.IsTakenDown);
    }

    [Fact(DisplayName = "P8-D10: restoring brings the listing back, is audited, and tells the owner")]
    public async Task RestoreListingAsync_RepublishesAndNotifies()
    {
        var property = Property.Create("شقة", "وصف", Guid.NewGuid(), ListingType.ForRent);
        property.TakeDown(AdminId, "reason");
        var auditLogs = new Mock<IAuditLogService>();
        var notifications = new Mock<INotificationService>();
        var service = BuildService(FakeProperties(property).Object, Mock.Of<IUnitOfWork>(), auditLogs.Object, notifications: notifications.Object);

        var result = await service.RestoreListingAsync(property.Id, "appeal accepted", AdminId, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(property.IsTakenDown);
        Assert.True(property.IsPublished);
        auditLogs.Verify(x => x.LogAsync(
            AdminId, AuditActions.ListingRestoredByAdmin, null,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(x => x.NotifyPropertyUpdateAsync(
            property.OwnerId, property.Id, property.Title, NotificationType.ListingRestored,
            It.IsAny<NotificationText>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreListingAsync_OnAListingThatIsNotTakenDown_IsAConflict()
    {
        var property = Property.Create("شقة", "وصف", Guid.NewGuid(), ListingType.ForRent);
        var service = BuildService(FakeProperties(property).Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.RestoreListingAsync(property.Id, null, AdminId, null, CancellationToken.None);

        Assert.True(result.Conflict);
    }

    private static Mock<IPropertyRepository> FakeProperties(Property property)
    {
        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByIdAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        properties.Setup(x => x.Update(property));
        return properties;
    }
}
