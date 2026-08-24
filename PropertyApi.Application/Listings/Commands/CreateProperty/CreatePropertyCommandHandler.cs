using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Commands.CreateProperty;

/// <summary>
/// Handles CreatePropertyCommand.
/// Uses the Property.Create() factory method (enforces domain invariants),
/// then saves via UnitOfWork.
///
/// BUG FIX: Was an empty "internal class CreatePropertyCommandHandler {}" — non-functional.
/// </summary>
public sealed class CreatePropertyCommandHandler
    : IRequestHandler<CreatePropertyCommand, Guid>
{
    private readonly IPropertyRepository _repo;
    private readonly IAgencyRepository _agencies;
    private readonly IUnitOfWork _uow;
    private readonly ILocationSuggestionService _locationSuggestions;
    private readonly ILogger<CreatePropertyCommandHandler> _logger;

    public CreatePropertyCommandHandler(
        IPropertyRepository repo,
        IAgencyRepository agencies,
        IUnitOfWork uow,
        ILocationSuggestionService locationSuggestions,
        ILogger<CreatePropertyCommandHandler> logger)
    {
        _repo = repo;
        _agencies = agencies;
        _uow = uow;
        _locationSuggestions = locationSuggestions;
        _logger = logger;
    }

    public async Task<Guid> Handle(
        CreatePropertyCommand request,
        CancellationToken cancellationToken)
    {
        await EnsureListingQuotaAvailableAsync(request.OwnerId, cancellationToken);

        // Factory method — the ONLY correct way to create a Property.
        // Throws DomainException if invariants are violated.
        var property = Property.Create(
            title: request.Title,
            description: request.Description,
            ownerId: request.OwnerId,
            listingType: request.ListingType,
            countryCode: request.CountryCode,
            currencyCode: request.CurrencyCode);

        // Set optional fields (public setters — no invariants to enforce)
        property.Street = request.Street;
        property.City = request.City;
        property.Region = request.Region;
        property.PostalCode = request.PostalCode;
        property.GovernorateId = request.GovernorateId;
        property.DistrictId = request.DistrictId;
        property.DistrictText = request.DistrictText;
        property.NeighborhoodId = request.NeighborhoodId;
        property.NeighborhoodText = request.NeighborhoodText;
        property.PropertyTypeId = request.PropertyTypeId;
        property.Latitude = request.Latitude;
        property.Longitude = request.Longitude;
        property.ColdRent = request.ColdRent;
        property.WarmRent = request.WarmRent;
        property.PurchasePrice = request.PurchasePrice;
        property.Deposit = request.Deposit;
        property.AdditionalCosts = request.AdditionalCosts;
        property.Rooms = request.Rooms;
        property.Area = request.Area;
        property.Floor = request.Floor;
        property.HasBalcony = request.HasBalcony;
        property.HasElevator = request.HasElevator;
        property.HasParkingSpace = request.HasParkingSpace;
        property.HeatingType = request.HeatingType;
        property.AvailableFrom = request.AvailableFrom;

        // Associate amenities if provided
        if (request.AmenityIds is { Count: > 0 })
        {
            foreach (var amenityId in request.AmenityIds)
            {
                property.PropertyAmenities.Add(new PropertyAmenity
                {
                    PropertyId = property.Id,
                    AmenityId = amenityId
                });
            }
        }

        // Attribute the listing to the owner's agency, if they belong to one. Read from the
        // owner's account rather than accepted from the request: a client that could name
        // the agency could attribute its listing to somebody else's office.
        //
        // Attribution is captured once, at creation. It is NOT re-derived later, so a
        // listing keeps the badge of the agency it was published under even if its owner
        // moves on — which is what the listing's history actually was.
        var ownerAccount = await _agencies.GetUserAccountAsync(request.OwnerId, cancellationToken);

        if (ownerAccount?.AgencyId is { } agencyId)
        {
            property.SetAgency(agencyId);
        }

        await _repo.AddAsync(property, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        // Best-effort — a manually-typed district/neighborhood name becomes a
        // suggestion for admin review (see LocationSuggestion's doc comment).
        // Deliberately never allowed to fail the actual listing save: this
        // runs after the property is already committed, and any exception
        // here is swallowed (with a log) rather than surfaced to the user.
        await TrySubmitLocationSuggestionsAsync(property, cancellationToken);

        return property.Id;
    }

    /// <summary>
    /// Enforces the free-tier active-listing limit.
    /// </summary>
    /// <remarks>
    /// There is no Subscription entity in this domain yet, so there is no way to ask "which
    /// plan is this user on" — every account is therefore treated as free tier. That is the
    /// honest state of the system, not a simplification: the moment a paid plan exists, this
    /// is the single place that has to learn about it, and the check becomes
    /// "quota = plan.ListingLimit" instead of the constant below.
    ///
    /// Consequence worth being explicit about: an existing owner who already holds more than
    /// the limit keeps every listing they have — nothing is retroactively removed — but
    /// cannot create another until they are back under it.
    /// </remarks>
    private async Task EnsureListingQuotaAvailableAsync(Guid ownerId, CancellationToken ct)
    {
        var activeListings = await _repo.CountActiveListingsByOwnerAsync(ownerId, ct);

        if (activeListings < ListingLifecyclePolicy.FreeTierActiveListingLimit)
            return;

        _logger.LogInformation(
            "Listing creation blocked by free-tier quota. OwnerId={OwnerId}, Active={Active}, Limit={Limit}",
            ownerId,
            activeListings,
            ListingLifecyclePolicy.FreeTierActiveListingLimit);

        throw new ConflictException(
            $"الخطة المجانية تسمح بإعلان واحد نشط فقط. لديك حالياً {activeListings}. " +
            "احذف إعلاناً قائماً أو رقِّ خطتك لإضافة إعلان جديد.");
    }

    private async Task TrySubmitLocationSuggestionsAsync(Property property, CancellationToken ct)
    {
        try
        {
            if (property.DistrictId is null && !string.IsNullOrWhiteSpace(property.DistrictText) && property.GovernorateId.HasValue)
            {
                await _locationSuggestions.SubmitDistrictSuggestionAsync(
                    property.GovernorateId.Value, property.DistrictText, property.OwnerId, property.Id, ct);
            }

            if (property.NeighborhoodId is null && !string.IsNullOrWhiteSpace(property.NeighborhoodText) && property.DistrictId.HasValue)
            {
                await _locationSuggestions.SubmitNeighborhoodSuggestionAsync(
                    property.DistrictId.Value, property.NeighborhoodText, property.OwnerId, property.Id, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to submit location suggestion for property {PropertyId} — listing was still saved successfully.",
                property.Id);
        }
    }
}

