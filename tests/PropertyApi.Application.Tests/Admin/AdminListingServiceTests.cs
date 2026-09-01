using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Admin.Services;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Tests.Admin;

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
