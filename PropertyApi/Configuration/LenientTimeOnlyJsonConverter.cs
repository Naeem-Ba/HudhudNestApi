using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PropertyApi.Configuration;

/// <summary>
/// System.Text.Json's built-in <see cref="TimeOnly"/> converter only accepts "HH:mm:ss[.fffffff]".
/// Every browser <c>&lt;input type="time"&gt;</c> (and most mobile clients) produce "HH:mm", so a
/// short-stay listing form submitting "14:00" was rejected with a model-binding error
/// ("The Dto field is required" / "The Check in time field is empty or invalid"). This converter
/// accepts both shapes on the way in and always writes the canonical "HH:mm:ss" on the way out,
/// so responses are unchanged.
/// </summary>
public sealed class LenientTimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] AcceptedFormats =
    [
        "HH':'mm",
        "HH':'mm':'ss",
        "HH':'mm':'ss'.'FFFFFFF",
    ];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A time value must be a string such as \"14:00\" or \"14:00:00\".");

        var text = reader.GetString()?.Trim();
        if (!string.IsNullOrEmpty(text)
            && TimeOnly.TryParseExact(text, AcceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return value;
        }

        throw new JsonException($"'{text}' is not a valid time. Use HH:mm or HH:mm:ss.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("HH':'mm':'ss", CultureInfo.InvariantCulture));
}
