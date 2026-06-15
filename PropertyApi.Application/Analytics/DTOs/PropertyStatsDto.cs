namespace PropertyApi.Application.Analytics.DTOs;

public sealed record PropertyStatsDto(
    Guid   PropertyId,
    string PropertyTitle,
    int    TotalVisitRequests,
    int    ConfirmedVisits,
    int    TotalMessages,
    int    TotalFavorites,
    int    TotalReviews,
    double AverageRating,
    decimal? AreaAveragePrice   // Average price/mÂ² for same city
);

public sealed record CityMarketInsightDto(
    string  City,
    string  CountryCode,
    decimal AvgPriceForRent,
    decimal AvgPriceForSale,
    int     TotalActiveListings
);

public sealed record MarketInsightsDto(
    IReadOnlyList<CityMarketInsightDto> Cities,
    DateTime GeneratedAt
);
