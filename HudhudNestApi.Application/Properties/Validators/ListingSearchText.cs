namespace HudhudNestApi.Application.Properties.Validators;

/// <summary>Shared checks for free text that ends up in a search predicate.</summary>
public static class ListingSearchText
{
    /// <summary>
    /// False when the text contains a NUL or another control character. PostgreSQL rejects NUL in
    /// text parameters, which surfaced as HTTP 500 for <c>?SearchTerm=a%00b</c> (QA Phase 7, P7-D03);
    /// no listing text legitimately contains one. Tab and newlines are allowed as separators.
    /// </summary>
    public static bool HasNoControlCharacters(string? value)
        => value is null || !value.Any(ch => char.IsControl(ch) && ch is not ('\t' or '\n' or '\r'));
}
