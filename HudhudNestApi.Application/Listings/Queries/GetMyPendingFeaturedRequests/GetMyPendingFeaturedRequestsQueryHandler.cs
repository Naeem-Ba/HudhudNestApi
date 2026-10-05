using MediatR;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Listings.Queries.GetMyPendingFeaturedRequests;

public sealed class GetMyPendingFeaturedRequestsQueryHandler
    : IRequestHandler<GetMyPendingFeaturedRequestsQuery, IReadOnlyList<PendingFeaturedRequestSummaryDto>>
{
    private readonly IListingFeeRepository _fees;

    public GetMyPendingFeaturedRequestsQueryHandler(IListingFeeRepository fees)
        => _fees = fees;

    public async Task<IReadOnlyList<PendingFeaturedRequestSummaryDto>> Handle(
        GetMyPendingFeaturedRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var fees = await _fees.GetPendingFeesByPayerAsync(
            request.OwnerId, TransactionType.FeaturedListingFee, cancellationToken);

        return fees
            .Select(fee => new PendingFeaturedRequestSummaryDto(
                fee.Id,
                fee.PropertyId,
                fee.AmountInUSD,
                (int)ListingLifecyclePolicy.FeaturedPeriod.TotalDays,
                fee.TransactedAt))
            .ToList();
    }
}
