using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings;
using HudhudNestApi.Infrastructure.Performance;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class PropertyGeoSearchRepository : IPropertyGeoSearchRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<PropertyGeoSearchRepository> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PropertyGeoSearchRepository(
        AppDbContext db,
        ILogger<PropertyGeoSearchRepository> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<PagedResult<GeoPropertySearchResultDto>> SearchNearbyAsync(
        GeoPropertySearchRequestDto filter,
        CancellationToken ct = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var radiusMeters = decimal.ToDouble(filter.RadiusKm) * 1000d;
        // Validated as required before this point (SearchPropertiesNearbyQueryValidator).
        var latitude = decimal.ToDouble(filter.Latitude ?? throw new ArgumentException("Latitude is required.", nameof(filter)));
        var longitude = decimal.ToDouble(filter.Longitude ?? throw new ArgumentException("Longitude is required.", nameof(filter)));

        var rates = !string.IsNullOrWhiteSpace(filter.CurrencyCode) && (filter.MinPrice.HasValue || filter.MaxPrice.HasValue)
            ? await PropertyRepository.LoadCurrencyRatesAsync(_db, ct)
            : CurrencyRates.Empty;

        // On the market = Available or Reserved inside the publication window (Property.OnMarket, in SQL).
        // The price range follows the list endpoint: with a currency it is read in that currency and
        // matches equivalent prices advertised in the other currencies. PriceFactor is "how many units
        // of the listing's currency equal one unit of the chosen currency" (1 when no currency is
        // chosen, or the listing is already in it); an unpriced currency yields NULL, which matches nothing.
        const string priceFactor = """
CASE
    WHEN @currencyCode IS NULL OR p."CurrencyCode" = @currencyCode THEN 1
    ELSE (SELECT r.rate FROM unnest(@rateCodes, @rateValues) AS r(code, rate) WHERE r.code = p."CurrencyCode")
         / @selectedRate
END
""";

        var filters = $"""
      p."IsDeleted" = FALSE
      AND p."IsPublished" = TRUE
      AND p."Status" IN ('Available', 'Reserved')
      AND (p."ExpiresAt" IS NULL OR p."ExpiresAt" > now())
      AND p."GeoLocation" IS NOT NULL
      AND (@countryCode IS NULL OR p."CountryCode" = @countryCode)
      AND (@city IS NULL OR p."City" ILIKE '%' || @city || '%')
      AND (@region IS NULL OR p."Region" ILIKE '%' || @region || '%')
      AND (@listingType IS NULL
           OR p."ListingType" = @listingType
           OR (@listingType <> 'ForRentAndSale' AND p."ListingType" = 'ForRentAndSale'))
      AND (@status IS NULL OR p."Status" = @status)
      AND (@condition IS NULL OR p."Condition" = @condition)
      AND (@ownerId IS NULL OR p."OwnerId" = @ownerId)
      AND (@minRooms IS NULL OR p."Rooms" >= @minRooms)
      AND (@maxRooms IS NULL OR p."Rooms" <= @maxRooms)
      AND (@minArea IS NULL OR p."Area" >= @minArea)
      AND (@maxArea IS NULL OR p."Area" <= @maxArea)
      AND (@hasBalcony IS NULL OR p."HasBalcony" = @hasBalcony)
      AND (@hasElevator IS NULL OR p."HasElevator" = @hasElevator)
      AND (@hasParkingSpace IS NULL OR p."HasParkingSpace" = @hasParkingSpace)
      AND (
            @minPrice IS NULL OR
            p."ColdRent" >= @minPrice * ({priceFactor}) OR
            p."WarmRent" >= @minPrice * ({priceFactor}) OR
            p."PurchasePrice" >= @minPrice * ({priceFactor})
          )
      AND (
            @maxPrice IS NULL OR
            (p."ColdRent" IS NULL OR p."ColdRent" <= @maxPrice * ({priceFactor})) AND
            (p."WarmRent" IS NULL OR p."WarmRent" <= @maxPrice * ({priceFactor})) AND
            (p."PurchasePrice" IS NULL OR p."PurchasePrice" <= @maxPrice * ({priceFactor}))
          )
""";

        var countSql = $"""
SELECT COUNT(*)
FROM "Properties" p
WHERE {filters}
  AND ST_DWithin(
        p."GeoLocation",
        ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
        @radiusMeters
      );
""";

        var sql = $"""
WITH nearest_candidates AS MATERIALIZED (
    SELECT p.*
    FROM "Properties" p
    WHERE {filters}
    ORDER BY p."GeoLocation" <->
             ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography
    LIMIT (@pageSize + @offset)
),
nearest AS (
    SELECT p.*
    FROM nearest_candidates p
    WHERE ST_DWithin(
            p."GeoLocation",
            ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
            @radiusMeters
          )
    ORDER BY p."GeoLocation" <->
             ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography
    OFFSET @offset
)
SELECT
        p."Id",
        p."Title",
        p."Description",
        p."Street",
        p."City",
        p."Region",
        p."CountryCode",
        p."PostalCode",
        p."Latitude",
        p."Longitude",
        p."ListingType",
        p."Status",
        p."ColdRent",
        p."WarmRent",
        p."PurchasePrice",
        p."CurrencyCode",
        p."Rooms",
        p."Area",
        p."OwnerId",
        p."CreatedAt",
        -- Effective featured state, evaluated the same way Property.CurrentlyFeatured does:
        -- the flag alone is not enough, because the sweep that clears a lapsed window only
        -- runs every six hours. Badge only — the ORDER BY below stays proximity-first.
        (
            p."IsFeatured"
            AND p."FeaturedUntil" IS NOT NULL
            AND p."FeaturedUntil" > now()
        ) AS "IsFeatured",
        p."FeaturedUntil",
        trim(coalesce(ua."FirstName", '') || ' ' || coalesce(ua."LastName", '')) AS "OwnerName",
        main_image."Url" AS "MainImageUrl",
        ST_Distance(
            p."GeoLocation",
            ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography
        ) AS "DistanceMeters"
FROM nearest p
LEFT JOIN "UserAccounts" ua ON ua."Id" = p."OwnerId"
LEFT JOIN LATERAL (
    SELECT pi."Url"
    FROM "PropertyImages" pi
    WHERE pi."PropertyId" = p."Id"
      AND pi."IsDeleted" = FALSE
    ORDER BY pi."IsMain" DESC, pi."SortOrder" ASC, pi."CreatedAt" ASC
    LIMIT 1
) main_image ON TRUE
ORDER BY "DistanceMeters" ASC, p."CreatedAt" DESC;
""";

        await using var connection = _db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var totalCount = await ExecuteCountAsync(connection, countSql, filter, latitude,
            longitude, radiusMeters, pageSize, offset, rates, ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        AddParameters(command, filter, latitude, longitude, radiusMeters, pageSize, offset, rates);

        var items = new List<GeoPropertySearchResultDto>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                items.Add(new GeoPropertySearchResultDto
                {
                    Id = reader.GetGuid(reader.GetOrdinal("Id")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    Description = reader.GetString(reader.GetOrdinal("Description")),
                    Street = reader.GetString(reader.GetOrdinal("Street")),
                    City = reader.GetString(reader.GetOrdinal("City")),
                    Region = GetNullableString(reader, "Region"),
                    CountryCode = reader.GetString(reader.GetOrdinal("CountryCode")),
                    PostalCode = GetNullableString(reader, "PostalCode"),
                    Latitude = GetNullableDecimal(reader, "Latitude"),
                    Longitude = GetNullableDecimal(reader, "Longitude"),
                    ListingType = reader.GetString(reader.GetOrdinal("ListingType")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    ColdRent = GetNullableDecimal(reader, "ColdRent"),
                    WarmRent = GetNullableDecimal(reader, "WarmRent"),
                    PurchasePrice = GetNullableDecimal(reader, "PurchasePrice"),
                    CurrencyCode = reader.GetString(reader.GetOrdinal("CurrencyCode")),
                    Rooms = GetNullableInt(reader, "Rooms"),
                    Area = GetNullableDecimal(reader, "Area"),
                    OwnerId = reader.GetGuid(reader.GetOrdinal("OwnerId")),
                    OwnerName = GetNullableString(reader, "OwnerName") ?? string.Empty,
                    MainImageUrl = GetNullableString(reader, "MainImageUrl"),
                    IsFeatured = reader.GetBoolean(reader.GetOrdinal("IsFeatured")),
                    FeaturedUntil = GetNullableDateTime(reader, "FeaturedUntil"),
                    DistanceMeters = Convert.ToDouble(reader["DistanceMeters"]),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                });
            }
        }
        catch (Exception ex)
        {
            // Privacy: never log the caller's exact device coordinates (a precise
            // lat/lng can pinpoint a home address). Round to ~11km so the log still
            // helps diagnose which region a failing query targeted.
            _logger.LogError(
                ex,
                "Geo property search failed near lat {LatitudeRounded}, lng {LongitudeRounded} (rounded), radius {RadiusKm} km.",
                Math.Round(filter.Latitude!.Value, 1),
                Math.Round(filter.Longitude!.Value, 1),
                filter.RadiusKm);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            RecordPerformanceDatabaseCommand(stopwatch.Elapsed);
        }

        return new PagedResult<GeoPropertySearchResultDto>
        {
            Items = items.AsReadOnly(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private async Task<int> ExecuteCountAsync(
        DbConnection connection,
        string sql,
        GeoPropertySearchRequestDto filter,
        double latitude,
        double longitude,
        double radiusMeters,
        int pageSize,
        int offset,
        CurrencyRates rates,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        AddParameters(command, filter, latitude, longitude, radiusMeters, pageSize, offset, rates);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }
        finally
        {
            stopwatch.Stop();
            RecordPerformanceDatabaseCommand(stopwatch.Elapsed);
        }
    }

    private static void AddParameters(
        IDbCommand command,
        GeoPropertySearchRequestDto filter,
        double latitude,
        double longitude,
        double radiusMeters,
        int pageSize,
        int offset,
        CurrencyRates rates)
    {
        AddParameter(command, "lat", latitude, DbType.Double);
        AddParameter(command, "lng", longitude, DbType.Double);
        AddParameter(command, "radiusMeters", radiusMeters, DbType.Double);
        AddParameter(command, "pageSize", pageSize, DbType.Int32);
        AddParameter(command, "offset", offset, DbType.Int32);

        AddParameter(command, "countryCode", NormalizeUpper(filter.CountryCode), DbType.String);
        AddParameter(command, "city", NormalizeLikeText(filter.City), DbType.String);
        AddParameter(command, "region", NormalizeLikeText(filter.Region), DbType.String);
        AddParameter(command, "listingType", filter.ListingType?.ToString(), DbType.String);
        AddParameter(command, "status", filter.Status?.ToString(), DbType.String);
        AddParameter(command, "condition", filter.Condition?.ToString(), DbType.String);
        AddParameter(command, "ownerId", filter.OwnerId, DbType.Guid);
        AddParameter(command, "minRooms", filter.MinRooms, DbType.Int32);
        AddParameter(command, "maxRooms", filter.MaxRooms, DbType.Int32);
        AddParameter(command, "minArea", filter.MinArea, DbType.Decimal);
        AddParameter(command, "maxArea", filter.MaxArea, DbType.Decimal);
        AddParameter(command, "hasBalcony", filter.HasBalcony, DbType.Boolean);
        AddParameter(command, "hasElevator", filter.HasElevator, DbType.Boolean);
        AddParameter(command, "hasParkingSpace", filter.HasParkingSpace, DbType.Boolean);
        AddParameter(command, "minPrice", filter.MinPrice, DbType.Decimal);
        AddParameter(command, "maxPrice", filter.MaxPrice, DbType.Decimal);

        // Exchange rates for the currency-aware price range: parallel arrays read with unnest().
        var codes = rates.UnitsPerUsd.Keys.ToArray();
        var values = codes.Select(code => rates.UnitsPerUsd[code]).ToArray();
        var selectedCode = NormalizeUpper(filter.CurrencyCode);
        decimal? selectedRate = selectedCode is not null && rates.TryGetRate(selectedCode, out var rate) ? rate : null;
        AddParameter(command, "currencyCode", selectedCode, DbType.String);
        AddParameter(command, "selectedRate", selectedRate, DbType.Decimal);
        command.Parameters.Add(new NpgsqlParameter("rateCodes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = codes });
        command.Parameters.Add(new NpgsqlParameter("rateValues", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = values });
    }

    private void RecordPerformanceDatabaseCommand(TimeSpan duration)
    {
        if (_httpContextAccessor.HttpContext?.Items[
                PerformanceDatabaseDiagnosticsPolicy.ContextItemName]
            is PerformanceDatabaseDiagnosticState state)
        {
            state.Record(duration);
        }
    }

    private static string? NormalizeUpper(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
    }

    /// <summary>Trimmed text with LIKE metacharacters escaped, so a typed % or _ is matched literally.</summary>
    private static string? NormalizeLikeText(string? value)
    {
        var text = NormalizeText(value);
        return text is null ? null : SearchTextNormalizer.EscapeLike(text);
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void AddParameter(
        IDbCommand command,
        string name,
        object? value,
        DbType dbType)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string? GetNullableString(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static decimal? GetNullableDecimal(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    private static int? GetNullableInt(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTime? GetNullableDateTime(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
