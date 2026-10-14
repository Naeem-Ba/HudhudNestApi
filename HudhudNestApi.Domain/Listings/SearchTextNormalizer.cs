using System.Text;

namespace HudhudNestApi.Domain.Listings;

/// <summary>
/// The one definition of how listing text and search words are folded before they are compared
/// (QA Phase 7, P7-D05). Arabic is written with optional vowel marks, stretched letters and
/// several interchangeable spellings of the same letter, so a person who types "شقة" must find a
/// listing titled "شقّه", and one who types "اراضي" must find "أراضي". Global listing sites do
/// the same fold (Bayut, Rightmove's location search, ImmoScout's umlaut folding).
///
/// The rules are expressed twice, deliberately side by side: <see cref="Normalize"/> for the
/// query words in C#, and <see cref="ToSqlExpression"/> for the stored generated column
/// <c>Properties.SearchText</c>. SearchTextNormalizerTests asserts that PostgreSQL and C# agree
/// on a sample of spellings, so the two cannot drift apart unnoticed.
///
/// Folding:
///  * Arabic vowel marks (U+064B..U+065F, U+0670) and the stretching letter tatweel (U+0640) are removed.
///  * Alef with hamza or madda, and alef wasla, become a plain alef; alef maqsura becomes yeh;
///    teh marbuta becomes heh; hamza on waw and on yeh become waw and yeh.
///  * Arabic-Indic and Persian digits become ASCII digits.
///  * Everything is lower-cased (Latin, German umlauts keep their letters).
/// </summary>
public static class SearchTextNormalizer
{
    /// <summary>PostgreSQL regex (the \uXXXX escapes are interpreted by the regex engine, not by C#).</summary>
    private const string RemovedRegexSql = @"[\u064B-\u065F\u0670\u0640]";

    private static readonly (char From, char To)[] Folds =
    [
        ('أ', 'ا'), ('إ', 'ا'), ('آ', 'ا'), ('ٱ', 'ا'),
        ('ى', 'ي'),
        ('ة', 'ه'),
        ('ؤ', 'و'),
        ('ئ', 'ي'),
        ('٠', '0'), ('١', '1'), ('٢', '2'), ('٣', '3'), ('٤', '4'),
        ('٥', '5'), ('٦', '6'), ('٧', '7'), ('٨', '8'), ('٩', '9'),
        ('۰', '0'), ('۱', '1'), ('۲', '2'), ('۳', '3'), ('۴', '4'),
        ('۵', '5'), ('۶', '6'), ('۷', '7'), ('۸', '8'), ('۹', '9')
    ];

    private static readonly Dictionary<char, char> FoldMap =
        Folds.ToDictionary(pair => pair.From, pair => pair.To);

    /// <summary>Folds one piece of text as the database folds <c>SearchText</c> (minus lower-casing).</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (IsRemoved(ch))
                continue;

            builder.Append(FoldMap.TryGetValue(ch, out var folded) ? folded : char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits a user's search text into the words that must all appear. Separators are any
    /// whitespace and the Arabic/Latin punctuation people type between words; empty fragments
    /// are dropped and the number of words is capped so a pathological query cannot build an
    /// unbounded predicate.
    /// </summary>
    public static IReadOnlyList<string> SplitWords(string? searchTerm, int maxWords = 8)
    {
        var normalized = Normalize(searchTerm);
        if (normalized.Length == 0)
            return Array.Empty<string>();

        return normalized
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Take(maxWords)
            .ToArray();
    }

    /// <summary>Escapes LIKE metacharacters so a typed % or _ is matched literally (escape character: backslash).</summary>
    public static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// The PostgreSQL expression for the generated column. Built from the same tables as
    /// <see cref="Normalize"/>. Only IMMUTABLE functions (translate, regexp_replace and the ||
    /// operator) so PostgreSQL accepts it in a stored generated column; the case-insensitivity
    /// comes from ILIKE at query time rather than from lower(), whose result depends on the
    /// database collation.
    /// </summary>
    public static string ToSqlExpression(params string[] columns)
    {
        var joined = string.Join(" || ' ' || ", columns.Select(column => $"coalesce({column}, '')"));
        var from = new string(Folds.Select(pair => pair.From).ToArray());
        var to = new string(Folds.Select(pair => pair.To).ToArray());
        return $"translate(regexp_replace({joined}, '{RemovedRegexSql}', '', 'g'), '{from}', '{to}')";
    }

    private static readonly char[] WordSeparators =
        [' ', '\t', '\r', '\n', ',', '،', ';', '؛', '.', '-', '/', '\\', '(', ')', '"', '\'', '«', '»', '!', '؟', '?'];

    private static bool IsRemoved(char ch)
        => (ch >= '\u064B' && ch <= '\u065F') || ch == '\u0670' || ch == '\u0640';
}
