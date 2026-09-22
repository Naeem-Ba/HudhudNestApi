using MediatR;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Search.Commands.CreateSavedSearch;

public sealed record CreateSavedSearchCommand(
    Guid UserId,
    string? Name,
    string? CountryCode,
    string? City,
    string? Region,
    int? GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    ListingType? ListingType,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? CurrencyCode,
    int? MinRooms,
    int? MaxRooms,
    decimal? MinArea,
    decimal? MaxArea
) : IRequest<Guid>;
