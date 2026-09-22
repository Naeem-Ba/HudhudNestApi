using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Admin.Services;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

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
        IPropertyRepository properties, IUnitOfWork unitOfWork, IAuditLogService auditLogs)
        => new(properties, unitOfWork, auditLogs, NullLogger<AdminListingService>.Instance);

    private static Mock<IPropertyRepository> FakeProperties(Property property)
    {
        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByIdAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        properties.Setup(x => x.Update(property));
        return properties;
    }
}
