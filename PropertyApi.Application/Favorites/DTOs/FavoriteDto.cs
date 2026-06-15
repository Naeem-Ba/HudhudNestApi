namespace PropertyApi.Application.Favorites.DTOs;

public sealed record FavoriteDto(
    Guid PropertyId,
    DateTime CreatedAt,
    FavoritePropertyDto Property);

public sealed record FavoritePropertyDto(
    string Title,
    string City,
    string CountryCode,
    decimal? ColdRent,
    decimal? PurchasePrice,
    string? MainImageUrl);

