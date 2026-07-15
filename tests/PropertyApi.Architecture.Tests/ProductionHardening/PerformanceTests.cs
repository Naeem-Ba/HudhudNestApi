namespace PropertyApi.Architecture.Tests.ProductionHardening;

public sealed class PerformanceTests
{
    [Fact(DisplayName = "Geo search must have pg_trgm migration and active property partial index")]
    public void Migrations_Should_Add_Trigram_And_Active_Search_Indexes()
    {
        var migration = ReadSource(
            "PropertyApi.Infrastructure",
            "Migrations",
            "20260611193000_AddPropertyTextSearchTrigramIndexes.cs");

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", migration);
        Assert.Contains("IX_Properties_City_Trigram", migration);
        Assert.Contains("IX_Properties_Region_Trigram", migration);
        Assert.Contains("IX_Properties_Active_Search", migration);
        Assert.Contains("USING GIN", migration);
    }

    [Fact(DisplayName = "Cloudinary media service must use IHttpClientFactory typed HttpClient")]
    public void Cloudinary_Should_Not_Use_Static_HttpClient()
    {
        var service = ReadSource("PropertyApi.Infrastructure", "Media", "CloudinaryMediaStorageService.cs");
        var mediaRegistration = ReadSource("PropertyApi.Infrastructure", "Media", "MediaInfrastructureRegistration.cs");

        Assert.DoesNotContain("static readonly HttpClient", service);
        Assert.Contains("HttpClient httpClient", service);
        Assert.Contains("AddHttpClient<CloudinaryMediaStorageService>", mediaRegistration);
    }

    [Fact(DisplayName = "Geo search validator must cap radius and maximum paging window")]
    public void GeoSearchValidator_Should_Cap_Radius_And_Maximum_Requested_Rows()
    {
        var validator = ReadSource(
            "PropertyApi.Application",
            "Listings",
            "Queries",
            "SearchPropertiesNearby",
            "SearchPropertiesNearbyQueryValidator.cs");

        Assert.Contains("LessThanOrEqualTo(50m)", validator);
        Assert.Contains("Page * filter.PageSize <= 1000", validator);
    }

    private static string ReadSource(params string[] relativePath)
    {
        var repoRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { repoRoot }.Concat(relativePath).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PropertyApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing PropertyApi.sln.");
    }
}
