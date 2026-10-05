using Moq;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Queries.GetMyPendingFeaturedRequests;
using HudhudNestApi.Domain.Transactions.Entities;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Tests.Listings;

public sealed class GetMyPendingFeaturedRequestsTests
{
    [Fact]
    public async Task ListsTheCallersPendingFeaturedFees_WithAmountAndPeriod()
    {
        var ownerId = Guid.NewGuid();
        var fee = Transaction.Create(
            Guid.NewGuid(), ownerId, Guid.Empty, TransactionType.FeaturedListingFee,
            5m, 1, 1m, TransactionPaymentMethod.BankTransfer);

        var fees = new Mock<IListingFeeRepository>();
        fees.Setup(x => x.GetPendingFeesByPayerAsync(
                ownerId, TransactionType.FeaturedListingFee, It.IsAny<CancellationToken>()))
            .ReturnsAsync([fee]);

        var result = await new GetMyPendingFeaturedRequestsQueryHandler(fees.Object)
            .Handle(new GetMyPendingFeaturedRequestsQuery(ownerId), CancellationToken.None);

        var row = Assert.Single(result);
        Assert.Equal(fee.Id, row.TransactionId);
        Assert.Equal(fee.PropertyId, row.PropertyId);
        Assert.Equal(5m, row.AmountUsd);
        Assert.Equal(30, row.FeaturedDays);
    }
}
