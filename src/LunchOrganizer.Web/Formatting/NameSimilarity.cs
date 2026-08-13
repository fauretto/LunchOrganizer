using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LunchOrganizer.Web.Formatting;

/// <summary>
/// Culture-agnostic, accent-and-case-insensitive name comparison used by the employee
/// autocomplete/"did you mean" flows.
/// </summary>
public static class NameSimilarity
{
    /// <summary>
    /// Lowercases, strips diacritics, and collapses/trims whitespace. Returns an empty string for
    /// null/empty/whitespace-only input rather than throwing.
    /// </summary>
    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var lowered = name.ToLowerInvariant();

        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var stripped = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                stripped.Append(c);
            }
        }

        var recomposed = stripped.ToString().Normalize(NormalizationForm.FormC);

        return Regex.Replace(recomposed, @"\s+", " ").Trim();
    }

    /// <summary>
    /// True if <paramref name="candidateFullName"/> looks like a plausible match for
    /// <paramref name="typedFragment"/>, tolerating accents/case and partial token overlap.
    /// </summary>
    public static bool IsSimilar(string candidateFullName, string typedFragment)
    {
        var normalizedCandidate = Normalize(candidateFullName);
        var normalizedFragment = Normalize(typedFragment);

        if (normalizedCandidate.Length == 0 || normalizedFragment.Length == 0)
        {
            return false;
        }

        var candidateTokens = normalizedCandidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var fragmentTokens = normalizedFragment.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var fragmentToken in fragmentTokens)
        {
            if (fragmentToken.Length < 2)
            {
                continue;
            }

            foreach (var candidateToken in candidateTokens)
            {
                if (candidateToken.StartsWith(fragmentToken, StringComparison.Ordinal) ||
                    fragmentToken.StartsWith(candidateToken, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return normalizedCandidate.Contains(normalizedFragment, StringComparison.Ordinal) ||
               normalizedFragment.Contains(normalizedCandidate, StringComparison.Ordinal);
    }
}
