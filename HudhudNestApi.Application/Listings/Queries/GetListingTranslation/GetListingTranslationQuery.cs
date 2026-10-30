using MediatR;
using HudhudNestApi.Application.Listings.Translations;
using HudhudNestApi.Domain.Listings.Translations;

namespace HudhudNestApi.Application.Listings.Queries.GetListingTranslation;

/// <summary>
/// Ensures (reads, or creates and stores) a listing's title/description in <paramref name="Language"/>.
/// Returns null when the listing does not exist or is not publicly viewable, which the controller maps to 404.
/// </summary>
public sealed record GetListingTranslationQuery(ListingRef Listing, string Language) : IRequest<ListingTranslationDto?>;

public sealed class GetListingTranslationQueryHandler : IRequestHandler<GetListingTranslationQuery, ListingTranslationDto?>
{
    private readonly IListingTranslationService _translations;

    public GetListingTranslationQueryHandler(IListingTranslationService translations) => _translations = translations;

    public Task<ListingTranslationDto?> Handle(GetListingTranslationQuery request, CancellationToken cancellationToken)
        => _translations.EnsureAsync(request.Listing, request.Language, cancellationToken);
}
