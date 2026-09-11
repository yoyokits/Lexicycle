using System.Globalization;
using System.Text;
using LexicycleCore.Models;

namespace LexicycleCore.Session;

/// <summary>
/// Decides whether a typed answer counts as correct.
///
/// Always trims, ignores case, and treats a leading article as optional — "das Haus"
/// and "Haus" are the same answer. Diacritics leniency is configurable.
/// </summary>
public sealed class AnswerComparer
{
    /// <param name="lenientDiacritics">
    /// When true (the default), "Cafe" matches "Café" and "Madchen" matches "Mädchen".
    /// German ASCII transliteration is accepted too, so "Maedchen" and "Strasse"
    /// match "Mädchen" and "Straße" — typing umlauts on a phone keyboard is awkward.
    /// When false, the answer must carry the exact diacritics.
    /// </param>
    public AnswerComparer(bool lenientDiacritics = true)
    {
        LenientDiacritics = lenientDiacritics;
    }

    public bool LenientDiacritics { get; }

    /// <summary>True when <paramref name="typed"/> matches any acceptable answer of the pair.</summary>
    public bool IsCorrect(WordPair pair, string? typed)
    {
        ArgumentNullException.ThrowIfNull(pair);

        if (string.IsNullOrWhiteSpace(typed))
        {
            return false;
        }

        var typedForms = CandidateForms(typed);

        foreach (var expected in pair.AcceptableAnswers)
        {
            if (CandidateForms(expected).Overlaps(typedForms))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every spelling of <paramref name="value"/> that should be treated as the same answer.
    /// </summary>
    private HashSet<string> CandidateForms(string value)
    {
        var basic = Collapse(value).ToLowerInvariant();
        var forms = new HashSet<string>(StringComparer.Ordinal) { basic };

        if (LenientDiacritics)
        {
            // "café" -> "cafe", "mädchen" -> "madchen"
            forms.Add(StripDiacritics(basic));

            // "mädchen" -> "maedchen", "straße" -> "strasse"
            forms.Add(TransliterateGerman(basic));
        }

        // A leading article is optional on both sides, so "das Haus" and "Haus" match
        // whichever way round they appear. Applied to every form already collected so it
        // composes with the diacritics rules ("die Straße" also matches "strasse").
        foreach (var form in forms.ToList())
        {
            if (StripLeadingArticle(form) is { } withoutArticle)
            {
                forms.Add(withoutArticle);
            }
        }

        return forms;
    }

    /// <summary>
    /// Definite and indefinite articles of the languages Lexicycle practises. English is
    /// included for the reverse direction.
    /// </summary>
    private static readonly HashSet<string> Articles = new(StringComparer.Ordinal)
    {
        // German
        "der", "die", "das", "den", "dem", "des",
        "ein", "eine", "einen", "einem", "einer", "eines",
        // Spanish
        "el", "la", "los", "las", "un", "una", "unos", "unas",
        // English
        "the", "a", "an",
    };

    /// <summary>
    /// Drops one leading article, or returns null when there is none to drop.
    /// A bare article is left alone — "die" on its own is the answer, not a prefix.
    /// </summary>
    private static string? StripLeadingArticle(string value)
    {
        var space = value.IndexOf(' ');
        if (space <= 0)
        {
            return null;
        }

        if (!Articles.Contains(value[..space]))
        {
            return null;
        }

        var remainder = value[(space + 1)..];
        return remainder.Length == 0 ? null : remainder;
    }

    /// <summary>Trims the ends and collapses any internal whitespace run to a single space.</summary>
    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var inWhitespace = false;

        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                inWhitespace = true;
                continue;
            }

            if (inWhitespace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            inWhitespace = false;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>Decomposes to base characters and drops the combining marks.</summary>
    private static string StripDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// The standard German ASCII fallback: ä→ae, ö→oe, ü→ue, ß→ss.
    /// Input is already lowercased, so only the lowercase forms are needed.
    /// </summary>
    private static string TransliterateGerman(string value)
    {
        var builder = new StringBuilder(value.Length + 4);

        foreach (var ch in value)
        {
            switch (ch)
            {
                case 'ä': builder.Append("ae"); break;
                case 'ö': builder.Append("oe"); break;
                case 'ü': builder.Append("ue"); break;
                case 'ß': builder.Append("ss"); break;
                default: builder.Append(ch); break;
            }
        }

        return builder.ToString();
    }
}
