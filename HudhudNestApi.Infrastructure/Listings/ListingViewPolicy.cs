using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using HudhudNestApi.Application.Listings.Views;

namespace HudhudNestApi.Infrastructure.Listings;

/// <summary>
/// Cooldown, retention and the keyed hash for the listing view counter.
/// Settings: <c>ListingViews:CooldownHours</c> (default 24, 1-168) and <c>ListingViews:RetentionDays</c>
/// (default 90, 30-365). The hash key is derived (HMAC with a fixed label) from
/// <c>Security:PhoneLookupHmacKey</c>, so no new secret has to be provisioned and the key is never
/// the same as the one used for phone lookups; <c>Jwt:Key</c> is the fallback.
/// </summary>
public sealed class ListingViewPolicy : IListingViewPolicy
{
    private const string KeyLabel = "hudhudnest:listing-views:v1";

    private readonly Lazy<byte[]> _key;

    public ListingViewPolicy(IConfiguration configuration)
    {
        var hours = Math.Clamp(configuration.GetValue("ListingViews:CooldownHours", 24), 1, 168);
        Cooldown = TimeSpan.FromHours(hours);
        RetentionDays = Math.Clamp(configuration.GetValue("ListingViews:RetentionDays", 90), 30, 365);
        _key = new Lazy<byte[]>(() => DeriveKey(configuration));
    }

    public TimeSpan Cooldown { get; }

    public int RetentionDays { get; }

    public long BucketFor(DateTime utc)
    {
        var seconds = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        return seconds / (long)Cooldown.TotalSeconds;
    }

    public string HashViewer(string identity)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        return Convert.ToHexString(HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static byte[] DeriveKey(IConfiguration configuration)
    {
        var source = configuration["Security:PhoneLookupHmacKey"];
        byte[] raw;
        if (!string.IsNullOrWhiteSpace(source))
        {
            try { raw = Convert.FromBase64String(source); }
            catch (FormatException) { raw = Encoding.UTF8.GetBytes(source); }
        }
        else
        {
            var jwt = configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwt))
                throw new InvalidOperationException("Listing views need Security:PhoneLookupHmacKey (or Jwt:Key) to be configured.");
            raw = Encoding.UTF8.GetBytes(jwt);
        }

        return HMACSHA256.HashData(raw, Encoding.UTF8.GetBytes(KeyLabel));
    }
}
