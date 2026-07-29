using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Listings.Enums;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;

var options = GeneratorOptions.Parse(args);
options.ValidateSafety();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ManifestPath))!);

var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(options.ConnectionString)
    .Options;

await using var db = new AppDbContext(dbOptions, new EphemeralDataProtectionProvider());
if (!await db.Database.CanConnectAsync())
    throw new InvalidOperationException("The isolated performance database is not reachable.");

var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
if (pendingMigrations.Any())
    throw new InvalidOperationException("Performance data generation requires all EF Core migrations to be applied.");

var postGisVersion = await db.Database
    .SqlQueryRaw<string>("SELECT PostGIS_Version() AS \"Value\"")
    .SingleAsync();
if (string.IsNullOrWhiteSpace(postGisVersion))
    throw new InvalidOperationException("PostGIS is not available in the performance database.");

var ownerMarker = $"PERF-{options.RunId}";
if (await db.UserAccounts.AnyAsync(x => x.LastName == ownerMarker))
    throw new InvalidOperationException("The exact performance run identifier already exists; use a unique PERF_RUN_ID.");

var generatedAt = DateTime.UtcNow;
var owners = Enumerable.Range(0, options.UserCount)
    .Select(index => UserAccount.Create(
        DeterministicGuid(options.Seed, "user", index),
        $"Perf{index:D5}",
        ownerMarker,
        generatedAt))
    .ToArray();

await db.UserAccounts.AddRangeAsync(owners);
await db.SaveChangesAsync();
db.ChangeTracker.Clear();

var random = new Random(options.Seed);
var imageCount = 0;
var cityCounts = new Dictionary<string, int>(StringComparer.Ordinal);
var anchor = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

for (var offset = 0; offset < options.PropertyCount; offset += options.BatchSize)
{
    var batchSize = Math.Min(options.BatchSize, options.PropertyCount - offset);
    var properties = new List<Property>(batchSize);
    var images = new List<PropertyImage>(batchSize);

    for (var batchIndex = 0; batchIndex < batchSize; batchIndex++)
    {
        var index = offset + batchIndex;
        var location = DatasetLocations.All[WeightedCluster(random)];
        var latitude = location.Latitude + ((decimal)random.NextDouble() - 0.5m) * location.Spread;
        var longitude = location.Longitude + ((decimal)random.NextDouble() - 0.5m) * location.Spread;
        var listingType = (ListingType)((index % 3) + 1);
        var property = Property.Create(
            $"PERF-{options.RunId}-PROPERTY-{index:D8}",
            $"Deterministic isolated performance property {index:D8}.",
            owners[index % owners.Length].Id,
            listingType,
            location.CountryCode,
            location.CurrencyCode);

        db.Entry(property).Property(nameof(property.Id)).CurrentValue =
            DeterministicGuid(options.Seed, "property", index);
        property.Street = $"Performance Street {index % 500}";
        property.City = location.City;
        property.Region = location.Region;
        property.PostalCode = $"{10000 + index % 89999}";
        property.Latitude = decimal.Round(latitude, 6);
        property.Longitude = decimal.Round(longitude, 6);
        property.Rooms = 1 + index % 8;
        property.Area = 35m + index % 280 + (index % 10) / 10m;
        property.Floor = index % 12;
        property.TotalFloors = 12;
        property.HasBalcony = index % 3 != 0;
        property.HasElevator = index % 4 != 0;
        property.HasParkingSpace = index % 5 == 0;
        property.Condition = (PropertyCondition)(index % 5);
        property.Status = (PropertyStatus)(index % 20 == 0 ? 1 : 0);
        property.ColdRent = listingType is ListingType.ForRent or ListingType.ForRentAndSale
            ? 250m + index % 4000
            : null;
        property.WarmRent = property.ColdRent + (property.ColdRent.HasValue ? 150m : 0m);
        property.PurchasePrice = listingType is ListingType.ForSale or ListingType.ForRentAndSale
            ? 40_000m + (index % 950_000)
            : null;
        property.CreatedAt = anchor.AddMinutes(index % 525_600);
        property.UpdatedAt = property.CreatedAt;
        db.Entry(property).Property(nameof(property.PublishedAt)).CurrentValue = property.CreatedAt;
        db.Entry(property).Property(nameof(property.UpdatedAt)).CurrentValue = property.CreatedAt;
        properties.Add(property);

        cityCounts[location.City] = cityCounts.GetValueOrDefault(location.City) + 1;

        if (index % 2 == 0)
        {
            var image = new PropertyImage
            {
                PropertyId = property.Id,
                Url = $"https://performance.invalid/images/{property.Id:N}.jpg",
                ThumbnailUrl = $"https://performance.invalid/images/{property.Id:N}-thumb.jpg",
                PublicId = $"perf/{options.RunId}/{property.Id:N}",
                AltText = "Isolated performance fixture",
                IsMain = true,
                SortOrder = 0,
                ImageType = PropertyImageType.General,
                CreatedAt = property.CreatedAt,
                UpdatedAt = property.CreatedAt,
                UploadedAt = property.CreatedAt
            };
            db.Entry(image).Property(nameof(image.Id)).CurrentValue =
                DeterministicGuid(options.Seed, "image", index);
            images.Add(image);
            imageCount++;
        }
    }

    await db.Properties.AddRangeAsync(properties);
    await db.PropertyImages.AddRangeAsync(images);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    Console.WriteLine($"Generated {offset + batchSize}/{options.PropertyCount} properties.");
}

await db.Database.ExecuteSqlRawAsync("ANALYZE \"Properties\"; ANALYZE \"PropertyImages\";");

await using var connection = new NpgsqlConnection(options.ConnectionString);
await connection.OpenAsync();
await using var sizeCommand = new NpgsqlCommand("SELECT pg_database_size(current_database())", connection);
var databaseSizeBytes = Convert.ToInt64(await sizeCommand.ExecuteScalarAsync());

var manifest = new
{
    runId = options.RunId,
    generatedAtUtc = generatedAt,
    propertyCount = options.PropertyCount,
    userCount = options.UserCount,
    imageCount,
    cityCount = cityCounts.Count,
    cityDistribution = cityCounts.OrderBy(x => x.Key),
    datasetSeed = options.Seed,
    databaseSizeBytes,
    postGisVersion,
    marker = $"PERF-{options.RunId}",
    manifestHash = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{options.Seed}:{options.PropertyCount}:{options.UserCount}:{imageCount}")))
};

await File.WriteAllTextAsync(
    options.ManifestPath,
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Dataset manifest: {Path.GetFullPath(options.ManifestPath)}");

static int WeightedCluster(Random random)
{
    var value = random.Next(100);
    return value switch
    {
        < 45 => 0,
        < 70 => 1,
        < 85 => 2,
        < 95 => 3,
        _ => 4
    };
}

static Guid DeterministicGuid(int seed, string kind, int index)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{kind}:{index}"));
    return new Guid(hash.AsSpan(0, 16));
}

internal sealed record LocationCluster(
    string City,
    string Region,
    string CountryCode,
    string CurrencyCode,
    decimal Latitude,
    decimal Longitude,
    decimal Spread);

internal static class DatasetLocations
{
    public static readonly LocationCluster[] All =
    [
        new("Damascus", "Damascus", "SY", "SYP", 33.5138m, 36.2765m, 0.18m),
        new("Aleppo", "Aleppo", "SY", "SYP", 36.2021m, 37.1343m, 0.22m),
        new("Homs", "Homs", "SY", "SYP", 34.7324m, 36.7137m, 0.30m),
        new("Latakia", "Latakia", "SY", "SYP", 35.5317m, 35.7901m, 0.25m),
        new("Berlin", "Berlin", "DE", "EUR", 52.5200m, 13.4050m, 0.40m)
    ];
}

internal sealed record GeneratorOptions(
    string Environment,
    string ConnectionString,
    string RunId,
    int PropertyCount,
    int UserCount,
    int Seed,
    int BatchSize,
    string ManifestPath)
{
    public static GeneratorOptions Parse(string[] args)
    {
        var values = args
            .Select((value, index) => (value, index))
            .Where(x => x.value.StartsWith("--", StringComparison.Ordinal) && x.index + 1 < args.Length)
            .ToDictionary(x => x.value[2..], x => args[x.index + 1], StringComparer.OrdinalIgnoreCase);

        string Required(string name, string environmentVariable)
            => values.GetValueOrDefault(name)
               ?? System.Environment.GetEnvironmentVariable(environmentVariable)
               ?? throw new InvalidOperationException($"{environmentVariable} is required.");

        int Number(string name, string environmentVariable, int fallback)
            => int.Parse(values.GetValueOrDefault(name)
                         ?? System.Environment.GetEnvironmentVariable(environmentVariable)
                         ?? fallback.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return new GeneratorOptions(
            Required("environment", "PERF_ENVIRONMENT"),
            Required("connection", "PERF_DATABASE_URL"),
            Required("run-id", "PERF_RUN_ID"),
            Number("properties", "PERF_DATASET_SIZE", 10_000),
            Number("users", "PERF_USER_COUNT", 250),
            Number("seed", "PERF_DATASET_SEED", 20260728),
            Number("batch-size", "PERF_GENERATOR_BATCH_SIZE", 1_000),
            values.GetValueOrDefault("manifest")
                ?? System.Environment.GetEnvironmentVariable("PERF_DATASET_MANIFEST")
                ?? "artifacts/performance/dataset-manifest.json");
    }

    public void ValidateSafety()
    {
        var allowed = new[] { "Local", "CI", "Performance", "Staging" };
        if (!allowed.Contains(Environment, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("PERF_ENVIRONMENT must be Local, CI, Performance, or Staging.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(RunId, "^[A-Za-z0-9_-]{6,80}$"))
            throw new InvalidOperationException("PERF_RUN_ID has an unsafe format.");
        if (PropertyCount is < 100 or > 1_000_000 || UserCount is < 1 or > 100_000)
            throw new InvalidOperationException("Requested dataset size is outside the approved safety bounds.");
        if (BatchSize is < 100 or > 5_000)
            throw new InvalidOperationException("Batch size must be between 100 and 5000.");

        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        var database = builder.Database ?? string.Empty;
        if (!(database.Contains("performance", StringComparison.OrdinalIgnoreCase)
              || database.Contains("perf", StringComparison.OrdinalIgnoreCase)
              || database.Contains("ci", StringComparison.OrdinalIgnoreCase)
              || database.Contains("test", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Refusing to generate data: database name must explicitly identify an isolated performance/CI/test database.");
        }
    }
}
