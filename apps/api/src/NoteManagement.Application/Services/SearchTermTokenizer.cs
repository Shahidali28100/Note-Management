using System.Text.RegularExpressions;

namespace NoteManagement.Application.Services;

/// <summary>
/// Tokenizes and sanitizes q into safe FTS terms (spec: "Search terms SHALL be treated as data,
/// never concatenated into a query string" / SDS §59). Stripping every non-letter/digit/apostrophe/
/// hyphen character means a token can never contain '"', '(', ')', '*', etc. — the characters that
/// carry special meaning inside a FORMSOF(...) predicate — so it can never break out of its quoted
/// phrase once SearchRepository wraps it. A token that sanitizes to empty is dropped entirely.
/// Public (rather than internal), same precedent as Infrastructure's JwtOptions — this codebase
/// avoids InternalsVisibleTo, so Tests.Unit can call this directly with hand-written strings.
/// </summary>
public static partial class SearchTermTokenizer
{
    [GeneratedRegex(@"[^\p{L}\p{Nd}'-]+")]
    private static partial Regex DisallowedChars();

    public static IReadOnlyList<string> Tokenize(string q) =>
        q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => DisallowedChars().Replace(t, string.Empty))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
