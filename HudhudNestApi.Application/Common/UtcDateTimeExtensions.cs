namespace HudhudNestApi.Application.Common;

/// <summary>
/// Timestamps are stored in <c>timestamp without time zone</c> columns and always hold UTC, but Npgsql hands them back
/// with <see cref="DateTimeKind.Unspecified"/>, which System.Text.Json writes without a trailing "Z". A browser reads
/// such a string as local time and shows the wrong hour or day (QA Phase 5, P5-D07). Use these on a DTO field whose
/// source is a stored value, so the JSON carries the offset.
/// </summary>
public static class UtcDateTimeExtensions
{
    public static DateTime AsUtc(this DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? AsUtc(this DateTime? value)
        => value?.AsUtc();
}
