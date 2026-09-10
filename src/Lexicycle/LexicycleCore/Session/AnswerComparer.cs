using System.Globalization;
using System.Text;
using LexicycleCore.Models;

namespace LexicycleCore.Session;

/// <summary>
/// Decides whether a typed answer counts as correct.
/// Always trims and ignores case; optionally ignores diacritics.
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
    /// Strict mode yields exactly one form, so comparison stays an exact (case-folded) match.
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

        return forms;
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
