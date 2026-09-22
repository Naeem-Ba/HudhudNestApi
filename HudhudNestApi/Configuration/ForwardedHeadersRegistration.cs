using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using AspNetCoreIPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace HudhudNestApi.Configuration;

public static class ForwardedHeadersRegistration
{
    public static IServiceCollection AddTrustedForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection("ForwardedHeaders");

        var isTestingOrCi =
            environment.EnvironmentName.Equals(
                "Testing",
                StringComparison.OrdinalIgnoreCase) ||
            environment.EnvironmentName.Equals(
                "CI",
                StringComparison.OrdinalIgnoreCase);

        var enabledByDefault =
            environment.IsProduction() ||
            environment.IsStaging();

        var enabled = section.GetValue(
            "Enabled",
            enabledByDefault && !isTestingOrCi);

        if (!enabled)
        {
            return services;
        }

        var knownProxies =
            section.GetSection("KnownProxies").Get<string[]>() ?? [];

        var knownNetworks =
            section.GetSection("KnownNetworks").Get<string[]>() ?? [];

        var allowedHosts =
            section.GetSection("AllowedHosts").Get<string[]>() ?? [];

        var forwardLimit =
            section.GetValue<int?>("ForwardLimit") ?? 1;

        var requiresExplicitTrustBoundary =
            environment.IsProduction() ||
            environment.IsStaging();

        if (requiresExplicitTrustBoundary &&
            knownProxies.Length == 0 &&
            knownNetworks.Length == 0)
        {
            throw new InvalidOperationException(
                "Forwarded Headers are enabled in Staging or Production, " +
                "but no trusted proxy or network allowlist is configured.");
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto;

            options.ForwardLimit = forwardLimit;

            // Render terminates TLS at its own edge (fronted by Cloudflare) and forwards to
            // Kestrel over plain HTTP; that edge is a multi-hop chain in which intermediate
            // hops do not symmetrically append to X-Forwarded-For and X-Forwarded-Proto.
            // RequireHeaderSymmetry=true made ForwardedHeadersMiddleware discard forwarded
            // scheme info entirely whenever the header counts mismatched -- logged as
            // "Parameter count mismatch between X-Forwarded-For and X-Forwarded-Proto" --
            // leaving Request.IsHttps false for every live request. UseHsts() is a no-op for
            // any request where IsHttps is false, so Strict-Transport-Security silently never
            // reached clients in Production despite IsProduction() correctly being true.
            options.RequireHeaderSymmetry = false;

            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            foreach (var proxy in knownProxies)
            {
                if (!IPAddress.TryParse(proxy, out var address))
                {
                    throw new InvalidOperationException($"Invalid ForwardedHeaders:KnownProxies value '{proxy}'.");
                }

                options.KnownProxies.Add(address);
            }

            foreach (var cidr in knownNetworks)
            {
                options.KnownNetworks.Add(ParseNetwork(cidr));
            }

            foreach (var host in allowedHosts.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                options.AllowedHosts.Add(host.Trim());
            }
        });

        return services;
    }

    private static AspNetCoreIPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var prefix) ||
            !int.TryParse(parts[1], out var prefixLength))
        {
            throw new InvalidOperationException(
                $"Invalid ForwardedHeaders:KnownNetworks CIDR value '{cidr}'. Expected format: 10.0.0.0/8.");
        }

        return new AspNetCoreIPNetwork(prefix, prefixLength);
    }
}
