namespace HudhudNestApi.Application.Listings.Translations;

/// <summary>
/// Cheap, deterministic source-language guess for listing text, so the provider is never called for text
/// that is already in the requested language. Script decides Arabic vs Latin (reliable even for short
/// titles); German vs English is decided by stop-words and a few listing words and returns
/// <see cref="Latin"/> when it cannot be sure (the provider's own detection then settles it, once, and the
/// answer is stored). Mixed or non-letter text returns null: no automatic translation.
/// </summary>
public static class ListingLanguageDetector
{
    /// <summary>Latin script, German vs English undecided.</summary>
    public const string Latin = "latin";

    private static readonly HashSet<string> German = new(StringComparer.Ordinal)
    {
        "der","die","das","und","ist","mit","für","fur","von","zu","zum","zur","im","in","am","auf","den","dem","ein","eine","einer","einem",
        "nicht","auch","bei","aus","nach","wir","sie","es","sehr","oder","als","hat","sind","werden","ab",
        "wohnung","zimmer","miete","kaufen","mieten","verkauf","verkaufen","haus","schlafzimmer","badezimmer","balkon","aufzug","stock",
        "etage","kaltmiete","warmmiete","nebenkosten","neubau","altbau","küche","kuche","garten","lage","ruhige","helle","renoviert","möbliert","mobliert"
    };

    private static readonly HashSet<string> English = new(StringComparer.Ordinal)
    {
        "the","and","is","with","for","of","to","in","on","at","a","an","this","that","are","from","by","it","has","have","not","or","as",
        "apartment","flat","room","rent","sale","buy","house","bedroom","bedrooms","bathroom","bathrooms","balcony","elevator","floor",
        "furnished","new","modern","spacious","quiet","located","close","near","view","garden","kitchen","available","villa","office","shop"
    };

    /// <summary>"ar", "de", "en", <see cref="Latin"/>, or null when the text cannot be classified.</summary>
    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        int arabic = 0, latin = 0;
        foreach (var ch in text)
        {
            if (ch is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ࢠ' and <= 'ࣿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿')
            {
                if (char.IsLetter(ch)) arabic++;
            }
            else if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 'À' and <= 'ɏ')
            {
                if (char.IsLetter(ch)) latin++;
            }
            else if (char.IsLetter(ch))
            {
                return null; // another script (Cyrillic, CJK...): not something this product translates automatically
            }
        }

        var letters = arabic + latin;
        if (letters < 3) return null;

        var arabicShare = (double)arabic / letters;
        if (arabicShare >= 0.6) return "ar";
        if (arabicShare > 0.2) return null; // genuinely mixed
        return DetectLatin(text);
    }

    private static string DetectLatin(string text)
    {
        int de = 0, en = 0, words = 0;
        foreach (var raw in text.Split(
                     [' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '(', ')', '/', '-', '"', '\'', '·', '|'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            words++;
            var w = raw.ToLowerInvariant();
            if (German.Contains(w)) de++;
            if (English.Contains(w)) en++;
            if (w.Contains('ß') || w.Contains('ä') || w.Contains('ö') || w.Contains('ü')) de += 2;
        }

        if (de >= 2 && de >= en + 2) return "de";
        if (en >= 2 && en >= de + 2) return "en";
        if (words >= 1 && de >= 1 && en == 0) return "de";
        if (words >= 1 && en >= 1 && de == 0) return "en";
        return Latin;
    }
}
