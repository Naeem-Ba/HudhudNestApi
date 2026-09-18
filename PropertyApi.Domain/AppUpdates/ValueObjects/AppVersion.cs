using System.Text.RegularExpressions;

namespace PropertyApi.Domain.AppUpdates.ValueObjects;

/// <summary>
/// A "major.minor.patch" app version, compared numerically (never as a string). String
/// comparison would put "1.10.0" before "1.9.9" because '1' &lt; '9' lexicographically — the
/// whole point of this type is that <see cref="CompareTo"/> compares Major/Minor/Patch as
/// integers instead, so 1.9.9 &lt; 1.10.0 &lt; 2.0.0 holds correctly.
/// </summary>
public readonly struct AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    // Anchored: exactly three numeric segments, no leading zeros (e.g. "01.2.3"), no
    // pre-release/build metadata suffix (e.g. "1.2.3-beta"), no whitespace.
    private static readonly Regex Pattern = new(
        @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$",
        RegexOptions.Compiled);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    private AppVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>The only entry point for parsing external/untrusted input (request query
    /// params, request bodies). Never throws.</summary>
    public static bool TryParse(string? input, out AppVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var match = Pattern.Match(input);
        if (!match.Success)
            return false;

        // Anchored digits-only groups always parse; int.Parse cannot throw here.
        var major = int.Parse(match.Groups["major"].Value);
        var minor = int.Parse(match.Groups["minor"].Value);
        var patch = int.Parse(match.Groups["patch"].Value);

        version = new AppVersion(major, minor, patch);
        return true;
    }

    /// <summary>Throwing variant, for reading a value back out of the database that was already
    /// validated on the way in (FluentValidation + the AppRelease domain invariant guarantee
    /// every persisted Version/MinimumSupportedVersion parses). External input must always go
    /// through <see cref="TryParse"/> instead.</summary>
    public static AppVersion Parse(string input)
    {
        if (!TryParse(input, out var version))
            throw new FormatException($"'{input}' is not a valid app version (expected major.minor.patch).");

        return version;
    }

    public int CompareTo(AppVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
            return majorComparison;

        var minorComparison = Minor.CompareTo(other.Minor);
        return minorComparison != 0 ? minorComparison : Patch.CompareTo(other.Patch);
    }

    public bool Equals(AppVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator ==(AppVersion left, AppVersion right) => left.Equals(right);
    public static bool operator !=(AppVersion left, AppVersion right) => !left.Equals(right);
    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;
}
