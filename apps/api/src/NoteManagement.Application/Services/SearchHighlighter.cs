using System.Text;
using System.Text.RegularExpressions;
using NoteManagement.Application.DTOs.Search;

namespace NoteManagement.Application.Services;

/// <summary>
/// Builds sentinel-delimited highlight excerpts (spec: "Search Result Highlighting"). Matches
/// terms as case-insensitive literal substrings — a deliberate, documented simplification from
/// SQL Server's inflectional FTS match (see plan.md architecture decisions). Pure/DB-free —
/// operates only on the note's own Title/Content plus the already-sanitized term list.
/// Public (rather than internal), same precedent as Infrastructure's JwtOptions — this codebase
/// avoids InternalsVisibleTo, so Tests.Unit can call this directly with hand-written strings.
/// </summary>
public static class SearchHighlighter
{
    /// <summary>
    /// Sentinel wrapping a matched term's start — a Unicode Private-Use-Area code point (never a
    /// real user-typed character), so a note's own content can never collide with it and it
    /// carries no markup meaning at all (SDS §44/§60). Written as a hex cast, not an escape
    /// sequence or a literal character, so the source file stays free of invisible/non-printable
    /// bytes. Public so Tests.Unit/Tests.Integration can assert on the exact delimiter without
    /// duplicating the literal value.
    /// </summary>
    public const char SentinelStart = (char)0xE000;

    /// <summary>Sentinel wrapping a matched term's end. See <see cref="SentinelStart"/>.</summary>
    public const char SentinelEnd = (char)0xE001;

    private const int ContentExcerptLength = 200;

    public static NoteHighlightDto Build(string title, string content, IReadOnlyList<string> terms)
    {
        var excerpt = ExtractExcerpt(content, terms);
        return new NoteHighlightDto(HighlightTerms(title, terms), HighlightTerms(excerpt, terms));
    }

    private static string ExtractExcerpt(string content, IReadOnlyList<string> terms)
    {
        if (content.Length <= ContentExcerptLength)
        {
            return content;
        }

        var firstMatch = terms
            .Select(t => content.IndexOf(t, StringComparison.OrdinalIgnoreCase))
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        var start = firstMatch < 0 ? 0 : Math.Max(0, firstMatch - (ContentExcerptLength / 2));
        start = Math.Min(start, Math.Max(0, content.Length - ContentExcerptLength));
        var length = Math.Min(ContentExcerptLength, content.Length - start);
        return content.Substring(start, length);
    }

    private static string HighlightTerms(string text, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0 || text.Length == 0)
        {
            return text;
        }

        var pattern = string.Join('|', terms.Select(Regex.Escape));
        var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);

        var result = new StringBuilder(text.Length + (terms.Count * 2));
        var lastEnd = 0;
        foreach (Match match in matches)
        {
            if (match.Index < lastEnd)
            {
                continue; // overlapping match — keep the earlier, already-emitted one.
            }

            result.Append(text, lastEnd, match.Index - lastEnd);
            result.Append(SentinelStart).Append(match.Value).Append(SentinelEnd);
            lastEnd = match.Index + match.Length;
        }

        result.Append(text, lastEnd, text.Length - lastEnd);
        return result.ToString();
    }
}
