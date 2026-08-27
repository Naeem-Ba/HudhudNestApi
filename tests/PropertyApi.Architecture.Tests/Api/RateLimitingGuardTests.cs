using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Controllers;

namespace PropertyApi.Architecture.Tests.Api;

/// <summary>
/// Guards B-7: every controller action reachable without a token must carry
/// [EnableRateLimiting], not just the handful the previous version of this test named
/// individually.
///
/// The earlier test only pinned a fixed list of endpoints ("Login must use auth-login",
/// "GetBySlug must use agencies-public", ...). That style catches nothing when a *new*
/// [AllowAnonymous] action is added -- it simply is not in the list, so no assertion ever
/// runs against it. This version enumerates every action the same way
/// PublicEndpointPolicyTests does (mirroring [AllowAnonymous] resolution, including
/// controller-level attributes, exactly as ASP.NET Core's authorization/endpoint metadata
/// does) and asserts each one carries [EnableRateLimiting], with a small, explicit,
/// documented exception list for endpoints that are anonymous but protected some other way.
///
/// A new [AllowAnonymous] action with no [EnableRateLimiting] and no entry in
/// <see cref="ExemptFromRateLimiting"/> fails <see cref="Every_AllowAnonymous_Endpoint_Should_Have_RateLimiting"/>.
/// An action removed from the exempt set that is still missing rate limiting fails the same
/// way -- the exemption has to be deliberately re-justified, not silently inherited.
/// </summary>
public sealed class RateLimitingGuardTests
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    /// <summary>
    /// Anonymous endpoints intentionally exempt from [EnableRateLimiting], with the reason
    /// each is safe to exempt. Every entry here must correspond to a real, independent
    /// control -- not "forgot to add the attribute". Removing an entry re-enables the
    /// enforcement test for that action; adding one requires a reason a reviewer can check.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExemptFromRateLimiting =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ObservabilitySyntheticController.Execute"] =
                "Staging-only: returns 404 unless IHostEnvironment.IsStaging() and " +
                "Staging:TestSupport:Enabled are both true. Not reachable in Production at all.",

            ["OperationalController.BuildInfo"] =
                "Gated by StagingTestSupportAuthorization (Staging + constant-time secret " +
                "compare, 404 otherwise) -- a stronger control than a per-IP rate limit, and " +
                "the same gate StagingTestSupportController uses below.",

            ["StagingTestSupportController.GetMedia"] =
                "Staging-only test-support endpoint gated by StagingTestSupportAuthorization " +
                "(secret header + 404-not-401 to avoid confirming its existence).",

            ["StagingTestSupportController.Cleanup"] =
                "Same StagingTestSupportAuthorization gate as GetMedia above."
        };

    [Fact(DisplayName = "Every [AllowAnonymous] endpoint has [EnableRateLimiting] or a documented exemption")]
    public void Every_AllowAnonymous_Endpoint_Should_Have_RateLimiting()
    {
        var missing = AnonymousEndpoints()
            .Where(endpoint => !HasEnableRateLimiting(endpoint) && !ExemptFromRateLimiting.ContainsKey(Describe(endpoint)))
            .Select(Describe)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These [AllowAnonymous] endpoints have no [EnableRateLimiting] policy and no " +
            "entry in RateLimitingGuardTests.ExemptFromRateLimiting. Either add " +
            "[EnableRateLimiting(\"<policy>\")] (registering the policy in Program.cs and " +
            "RedisRateLimitingDefaults if it is new), or add a justified, reviewed exemption:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, missing));
    }

    [Fact(DisplayName = "Exempt endpoints are still anonymous and still exist")]
    public void ExemptEndpoints_Should_Still_Be_Anonymous_And_Exist()
    {
        var actualAnonymous = AnonymousEndpoints().Select(Describe).ToHashSet(StringComparer.Ordinal);

        var stale = ExemptFromRateLimiting.Keys
            .Except(actualAnonymous)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These entries in ExemptFromRateLimiting no longer exist or are no longer " +
            "[AllowAnonymous] -- remove the stale exemption:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, stale));
    }

    [Fact(DisplayName = "Exempt endpoints do not accidentally carry [EnableRateLimiting]")]
    public void ExemptEndpoints_Should_Not_Carry_RateLimiting()
    {
        // Not a correctness bug either way, but an exempt endpoint that already has
        // [EnableRateLimiting] means the exemption is stale documentation -- it should be
        // removed from the list so the real enforcement test covers the endpoint again.
        var redundant = AnonymousEndpoints()
            .Where(endpoint => ExemptFromRateLimiting.ContainsKey(Describe(endpoint)) && HasEnableRateLimiting(endpoint))
            .Select(Describe)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            redundant.Count == 0,
            "These endpoints carry [EnableRateLimiting] already, so their entry in " +
            "ExemptFromRateLimiting is stale -- remove it:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, redundant));
    }

    /// <summary>
    /// Supplements the general enforcement test above with exact-policy pins on the most
    /// sensitive auth endpoints: it is not enough that *some* policy is attached (which is
    /// all the general test checks), a credential-stuffing target specifically needs its own
    /// tight policy rather than, say, the shared 120/min public-read cadence.
    /// </summary>
    [Theory(DisplayName = "Sensitive auth endpoints use their specific rate-limit policy, not a shared one")]
    [InlineData(typeof(AuthController), nameof(AuthController.Login), "auth-login")]
    [InlineData(typeof(AuthController), nameof(AuthController.Register), "auth-register")]
    [InlineData(typeof(AuthController), nameof(AuthController.Refresh), "auth-refresh")]
    [InlineData(typeof(AuthController), nameof(AuthController.SocialLoginGoogle), "auth-login")]
    [InlineData(typeof(AuthController), nameof(AuthController.SocialLoginApple), "auth-login")]
    [InlineData(typeof(PropertiesController), nameof(PropertiesController.GetAll), "public-search")]
    [InlineData(typeof(PropertiesController), nameof(PropertiesController.SearchNearby), "geo-search")]
    [InlineData(typeof(AgenciesController), nameof(AgenciesController.GetBySlug), "agencies-public")]
    public void Endpoint_Should_Have_Specific_RateLimit_Policy(Type controller, string methodName, string expectedPolicy)
    {
        var method = controller.GetMethod(methodName);
        Assert.NotNull(method);

        var attribute = Assert.Single(method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>());

        Assert.Equal(expectedPolicy, attribute.PolicyName);
    }

    [Fact(DisplayName = "Program.cs must define local fallback auth policies and enable Redis rate limiting")]
    public void Program_Should_Define_Auth_RateLimit_Policies()
    {
        var repoRoot = FindRepositoryRoot();
        var programFile = Path.Combine(repoRoot, "PropertyApi", "Program.cs");
        var source = File.ReadAllText(programFile);

        Assert.Contains("auth-login", source);
        Assert.Contains("PermitLimit = 10", source);
        Assert.Contains("TimeSpan.FromMinutes(1)", source);
        Assert.Contains("auth-register", source);
        Assert.Contains("PermitLimit = 5", source);
        Assert.Contains("TimeSpan.FromMinutes(10)", source);
        Assert.Contains("UseRedisRateLimiting", source);
        Assert.Contains("AddPropertyApiRedisRateLimiting", source);
    }

    /// <summary>
    /// Every action that is [AllowAnonymous] once controller-level attributes are folded in --
    /// the same resolution PublicEndpointPolicyTests uses, which in turn mirrors how ASP.NET
    /// Core actually resolves [AllowAnonymous]/[Authorize] endpoint metadata (inherit: true,
    /// checked at both the method and the declaring type). A plain
    /// GetCustomAttributes(inherit: false) at the method level alone would miss every action
    /// that is anonymous only because its *controller* carries the attribute, producing a
    /// false negative (an unprotected endpoint the test never looks at) -- the opposite of
    /// what this guard exists to catch.
    /// </summary>
    private static IEnumerable<MethodInfo> AnonymousEndpoints()
        => ApiAssembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .Where(HasAllowAnonymous);

    private static bool HasAllowAnonymous(MethodInfo endpoint)
        => endpoint.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null ||
           endpoint.DeclaringType!.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;

    /// <summary>
    /// [EnableRateLimiting] is only ever placed on the action itself in this codebase (never
    /// at controller level today), but resolved with inherit: true regardless so a future
    /// controller-level policy is still picked up correctly instead of producing a false
    /// "missing" failure.
    /// </summary>
    private static bool HasEnableRateLimiting(MethodInfo endpoint)
        => endpoint.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true) is not null ||
           endpoint.DeclaringType!.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true) is not null;

    private static string Describe(MethodInfo endpoint)
        => $"{endpoint.DeclaringType!.Name}.{endpoint.Name}";

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
