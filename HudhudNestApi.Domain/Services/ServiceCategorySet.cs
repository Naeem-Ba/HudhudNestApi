using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Domain.Services;

/// <summary>
/// Normalizes the category and governorate lists stored as Postgres arrays on applications and
/// providers: distinct, sorted, enum names as text (same string form ServiceOffering.Category is
/// stored in), so two lists with the same members always compare and serialize the same.
/// </summary>
public static class ServiceCategorySet
{
    public static string[] ToNames(IEnumerable<ServiceCategory>? categories) =>
        (categories ?? [])
            .Where(c => Enum.IsDefined(c))
            .Distinct()
            .OrderBy(c => (int)c)
            .Select(c => c.ToString())
            .ToArray();

    public static IReadOnlyList<ServiceCategory> FromNames(IEnumerable<string>? names) =>
        (names ?? [])
            .Select(n => Enum.TryParse<ServiceCategory>(n, ignoreCase: false, out var c) ? (ServiceCategory?)c : null)
            .Where(c => c is not null)
            .Select(c => c!.Value)
            .Distinct()
            .OrderBy(c => (int)c)
            .ToList();

    public static int[] NormalizeIds(IEnumerable<int>? ids) =>
        (ids ?? []).Where(id => id > 0).Distinct().Order().ToArray();
}
