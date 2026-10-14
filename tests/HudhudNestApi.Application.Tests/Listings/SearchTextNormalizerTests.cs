using HudhudNestApi.Domain.Listings;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// The folding rules of keyword search (QA Phase 7, P7-D05). The same rules run inside PostgreSQL
/// for the stored column; the Postgres side is pinned against these by an integration test.
/// </summary>
public sealed class SearchTextNormalizerTests
{
    [Theory(DisplayName = "Spelling variants of one Arabic word fold to the same text")]
    [InlineData("شقة", "شقه")]                // teh marbuta / heh
    [InlineData("أراضي", "اراضي")]            // alef with hamza
    [InlineData("إيجار", "ايجار")]            // alef with hamza below
    [InlineData("آمنة", "امنه")]              // alef with madda + teh marbuta
    [InlineData("مستشفى", "مستشفي")]          // alef maqsura / yeh
    [InlineData("مُفَروشَة", "مفروشه")]        // vowel marks
    [InlineData("مـــوقع", "موقع")]            // tatweel
    [InlineData("مسؤول", "مسوول")]            // hamza on waw
    [InlineData("بيئة", "بييه")]              // hamza on yeh
    public void ArabicVariants_FoldTogether(string typed, string folded)
    {
        Assert.Equal(folded, SearchTextNormalizer.Normalize(typed));
    }

    [Fact(DisplayName = "Arabic-Indic and Persian digits become ASCII digits")]
    public void Digits_AreFolded()
    {
        Assert.Equal("غرفة 3 طابق 12", SearchTextNormalizer.Normalize("غرفة ٣ طابق ۱۲").Replace("غرفه", "غرفة"));
        Assert.Equal("0123456789", SearchTextNormalizer.Normalize("٠١٢٣٤٥٦٧٨٩"));
        Assert.Equal("0123456789", SearchTextNormalizer.Normalize("۰۱۲۳۴۵۶۷۸۹"));
    }

    [Fact]
    public void Latin_IsLowerCased_AndUmlautsKeepTheirLetters()
    {
        Assert.Equal("modern flat in köln", SearchTextNormalizer.Normalize("Modern FLAT in Köln"));
    }

    [Fact]
    public void NullOrEmpty_IsEmpty()
    {
        Assert.Equal(string.Empty, SearchTextNormalizer.Normalize(null));
        Assert.Equal(string.Empty, SearchTextNormalizer.Normalize(""));
    }

    [Fact(DisplayName = "Words are split on spaces and punctuation, folded, de-duplicated and capped")]
    public void SplitWords_SplitsFoldsDedupesAndCaps()
    {
        Assert.Equal(["modern", "flat"], SearchTextNormalizer.SplitWords("  Modern,   flat  "));
        Assert.Equal(["شقه", "مفروشه"], SearchTextNormalizer.SplitWords("شقة، مفروشة"));
        Assert.Equal(["flat"], SearchTextNormalizer.SplitWords("flat FLAT Flat"));
        Assert.Empty(SearchTextNormalizer.SplitWords("   "));
        Assert.Empty(SearchTextNormalizer.SplitWords(null));
        Assert.Equal(8, SearchTextNormalizer.SplitWords("a b c d e f g h i j k l").Count);
    }

    [Theory(DisplayName = "LIKE metacharacters in a typed word are matched literally")]
    [InlineData("100%", "100\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("plain", "plain")]
    public void EscapeLike_EscapesWildcards(string typed, string escaped)
    {
        Assert.Equal(escaped, SearchTextNormalizer.EscapeLike(typed));
    }

    [Fact(DisplayName = "The SQL expression covers every column and uses only immutable functions")]
    public void SqlExpression_IsBuiltFromTheSameTablesAndIsImmutable()
    {
        var sql = SearchTextNormalizer.ToSqlExpression("\"Title\"", "\"Description\"");

        Assert.Contains("coalesce(\"Title\", '')", sql);
        Assert.Contains("coalesce(\"Description\", '')", sql);
        Assert.StartsWith("translate(regexp_replace(", sql);
        // concat_ws() and lower() are not accepted in a generated column on every PostgreSQL build.
        Assert.DoesNotContain("concat_ws", sql);
        Assert.DoesNotContain("lower(", sql);
        // from/to character lists must be the same length or translate() silently drops characters.
        var match = System.Text.RegularExpressions.Regex.Match(sql, "'g'\\), '(?<from>.*)', '(?<to>.*)'\\)$");
        Assert.True(match.Success);
        Assert.Equal(match.Groups["from"].Value.Length, match.Groups["to"].Value.Length);
    }
}
