using System.Security.Cryptography;
using System.Text;

namespace HudhudNestApi.Application.Listings.Translations;

/// <summary>
/// SHA-256 (lower-case hex) of the UTF-8 bytes of the stored text, exactly as stored: no case folding, no
/// whitespace or Unicode normalisation, so a one-character edit - or an Arabic diacritic - changes the hash.
/// The database computes the same value (<c>encode(sha256(convert_to(col,'UTF8')),'hex')</c>) when a
/// translation is claimed and committed, which is how a result for edited text is refused atomically.
/// </summary>
public static class ListingTextFingerprint
{
    public static string Compute(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
