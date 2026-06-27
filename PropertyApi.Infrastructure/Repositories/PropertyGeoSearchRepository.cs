using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class PropertyGeoSearchRepository : IPropertyGeoSearchRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<PropertyGeoSearchRepository> _logger;

    public PropertyGeoSearchRepository(
        AppDbContext db,
        ILogger<PropertyGeoSearchRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<GeoPropertySearchResultDto>> SearchNearbyAsync(
        GeoPropertySearchRequestDto filter,
        CancellationToken ct = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var radiusMeters = decimal.ToDouble(filter.RadiusKm) * 1000d;
        var latitude = decimal.ToDouble(filter.Latitude);
        var longitude = decimal.ToDouble(filter.Longitude);

        const string sql = """
WITH nearby AS (
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
        trim(coalesce(u."FirstName", '') || ' ' || coalesce(u."LastName", '')) AS "OwnerName",
        main_image."Url" AS "MainImageUrl",
        ST_Distance(
            p."GeoLocation",
            ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography
        ) AS "DistanceMeters",
        COUNT(*) OVER() AS "TotalCount"
    FROM "Properties" p
    LEFT JOIN "Users" u ON u."Id" = p."OwnerId"
    LEFT JOIN LATERAL (
        SELECT pi."Url"
        FROM "PropertyImages" pi
        WHERE pi."PropertyId" = p."Id"
          AND pi."IsDeleted" = FALSE
        ORDER BY pi."IsMain" DESC, pi."SortOrder" ASC, pi."CreatedAt" ASC
        LIMIT 1
    ) main_image ON TRUE
    WHERE p."IsDeleted" = FALSE
      AND p."IsPublished" = TRUE
      AND (p."ExpiresAt" IS NULL OR p."ExpiresAt" > now())
      AND p."GeoLocation" IS NOT NULL
      AND ST_DWithin(
            p."GeoLocation",
            ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
            @radiusMeters
          )
      AND (@countryCode IS NULL OR p."CountryCode" = @countryCode)
      AND (@city IS NULL OR p."City" ILIKE '%' || @city || '%')
      AND (@region IS NULL OR p."Region" ILIKE '%' || @region || '%')
      AND (@listingType IS NULL OR p."ListingType" = @listingType)
      AND (@status IS NULL OR p."Status" = @status)
      AND (@condition IS NULL OR p."Condition" = @condition)
      AND (@currencyCode IS NULL OR p."CurrencyCode" = @currencyCode)
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
            p."ColdRent" >= @minPrice OR
            p."WarmRent" >= @minPrice OR
            p."PurchasePrice" >= @minPrice
          )
      AND (
            @maxPrice IS NULL OR
            (p."ColdRent" IS NULL OR p."ColdRent" <= @maxPrice) AND
            (p."WarmRent" IS NULL OR p."WarmRent" <= @maxPrice) AND
            (p."PurchasePrice" IS NULL OR p."PurchasePrice" <= @maxPrice)
          )
)
SELECT *
FROM nearby
ORDER BY "DistanceMeters" ASC, "CreatedAt" DESC
LIMIT @pageSize OFFSET @offset;
""";

        await using var connection = _db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        AddParameter(command, "lat", latitude);
        AddParameter(command, "lng", longitude);
        AddParameter(command, "radiusMeters", radiusMeters);
        AddParameter(command, "pageSize", pageSize);
        AddParameter(command, "offset", offset);

        AddParameter(command, "countryCode", NormalizeUpper(filter.CountryCode));
        AddParameter(command, "city", NormalizeText(filter.City));
        AddParameter(command, "region", NormalizeText(filter.Region));
        AddParameter(command, "listingType", filter.ListingType?.ToString());
        AddParameter(command, "status", filter.Status?.ToString());
        AddParameter(command, "condition", filter.Condition?.ToString());
        AddParameter(command, "currencyCode", NormalizeUpper(filter.CurrencyCode));
        AddParameter(command, "ownerId", filter.OwnerId);
        AddParameter(command, "minRooms", filter.MinRooms);
        AddParameter(command, "maxRooms", filter.MaxRooms);
        AddParameter(command, "minArea", filter.MinArea);
        AddParameter(command, "maxArea", filter.MaxArea);
        AddParameter(command, "hasBalcony", filter.HasBalcony);
        AddParameter(command, "hasElevator", filter.HasElevator);
        AddParameter(command, "hasParkingSpace", filter.HasParkingSpace);
        AddParameter(command, "minPrice", filter.MinPrice);
        AddParameter(command, "maxPrice", filter.MaxPrice);

        var items = new List<GeoPropertySearchResultDto>();
        var totalCount = 0;
        var originalPageSize = pageSize;
        var originalOffset = offset;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                if (totalCount == 0 && !reader.IsDBNull(reader.GetOrdinal("TotalCount")))
                {
                    totalCount = Convert.ToInt32(reader["TotalCount"]);
                }

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
                    DistanceMeters = Convert.ToDouble(reader["DistanceMeters"]),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Geo property search failed for lat {Latitude}, lng {Longitude}, radius {RadiusKm} km.",
                filter.Latitude,
                filter.Longitude,
                filter.RadiusKm);
            throw;
        }

        if (items.Count == 0 && page > 1)
        {
            totalCount = await ReadTotalCountFromFirstMatchingRowAsync(
                command,
                originalPageSize,
                originalOffset,
                ct);
        }

        return new PagedResult<GeoPropertySearchResultDto>
        {
            Items = items.AsReadOnly(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }


    private static async Task<int> ReadTotalCountFromFirstMatchingRowAsync(
        IDbCommand command,
        int originalPageSize,
        int originalOffset,
        CancellationToken ct)
    {
        SetParameterValue(command, "pageSize", 1);
        SetParameterValue(command, "offset", 0);

        try
        {
            await using var reader = await ((DbCommand)command).ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct) && !reader.IsDBNull(reader.GetOrdinal("TotalCount")))
            {
                return Convert.ToInt32(reader["TotalCount"]);
            }

            return 0;
        }
        finally
        {
            SetParameterValue(command, "pageSize", originalPageSize);
            SetParameterValue(command, "offset", originalOffset);
        }
    }

    private static void SetParameterValue(IDbCommand command, string name, object value)
    {
        foreach (IDbDataParameter parameter in command.Parameters)
        {
            if (string.Equals(parameter.ParameterName, name, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = value;
                return;
            }
        }

        throw new InvalidOperationException($"SQL parameter '{name}' was not found.");
    }

    private static string? NormalizeUpper(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
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
}
