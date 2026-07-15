namespace PropertyApi.Architecture.Tests.Api;

public sealed class MiddlewareOrderGuardTests
{
    [Fact(DisplayName = "Program middleware order must preserve routing security and operational guarantees")]
    public void Program_Should_Preserve_Middleware_Order()
    {
        var source = ReadSource("PropertyApi", "Program.cs");

        AssertBefore(source, "app.UseForwardedHeaders", "app.UseHsts");
        AssertBefore(source, "app.UseForwardedHeaders", "app.UseHttpsRedirection");
        AssertBefore(source, "app.UseForwardedHeaders", "app.UsePropertyApiObservability");
        AssertBefore(source, "app.UsePropertyApiObservability", "app.UseMiddleware<ExceptionHandlingMiddleware>");
        AssertBefore(source, "app.UseForwardedHeaders", "app.UseMiddleware<ExceptionHandlingMiddleware>");

        AssertBefore(source, "app.UseMiddleware<ExceptionHandlingMiddleware>", "app.UsePropertyApiSecurityHeaders");
        AssertBefore(source, "app.UsePropertyApiSecurityHeaders", "app.UseStaticFiles");
        AssertBefore(source, "app.UseStaticFiles", "app.UseRouting");

        AssertBefore(source, "app.UseRouting", "app.UseCors");
        AssertBefore(source, "app.UseCors", "app.UseAuthentication");
        AssertBefore(source, "app.UseAuthentication", "app.UseRedisRateLimiting");
        AssertBefore(source, "app.UseAuthentication", "app.UseRateLimiter");
        AssertBefore(source, "app.UseRedisRateLimiting", "app.UseCookieCsrfProtection");
        AssertBefore(source, "app.UseRateLimiter", "app.UseCookieCsrfProtection");
        AssertBefore(source, "app.UseCookieCsrfProtection", "app.UseAuthorization");

        AssertBefore(source, "app.UseAuthorization", "app.UseOutputCache");
        AssertBefore(source, "app.UseOutputCache", "app.MapOperationalHealthEndpoints");
        AssertBefore(source, "app.MapOperationalHealthEndpoints", "app.MapControllers");
        AssertBefore(source, "app.MapControllers", "app.MapHub<NotificationHub>");
    }

    private static void AssertBefore(
        string source,
        string first,
        string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, $"Could not find '{first}' in Program.cs.");
        Assert.True(secondIndex >= 0, $"Could not find '{second}' in Program.cs.");
        Assert.True(
            firstIndex < secondIndex,
            $"Expected '{first}' to appear before '{second}' in Program.cs.");
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
