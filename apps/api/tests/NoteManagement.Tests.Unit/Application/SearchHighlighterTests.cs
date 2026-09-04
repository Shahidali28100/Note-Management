using NoteManagement.Application.Services;

namespace NoteManagement.Tests.Unit.Application;

[TestClass]
public sealed class SearchHighlighterTests
{
    [TestMethod]
    public void Build_TitleMatch_WrapsTermWithSentinels()
    {
        var result = SearchHighlighter.Build("Elephant Safari", "Content", new[] { "elephant" });

        var expected = $"{SearchHighlighter.SentinelStart}Elephant{SearchHighlighter.SentinelEnd} Safari";
        Assert.AreEqual(expected, result.Title);
    }

    [TestMethod]
    public void Build_ContentMatch_ReturnsExcerptCenteredOnMatch()
    {
        // Content well over the 200-char excerpt bound, with the match far from the start —
        // the excerpt must be centered on the match, not just the first 200 characters.
        var filler = new string('x', 300);
        var content = filler + "elephant" + filler;

        var result = SearchHighlighter.Build("Title", content, new[] { "elephant" });

        // The 200-char bound applies to the excerpt before highlighting is applied — the sentinel
        // pair itself adds 2 characters per match, so the unwrapped length (sentinels stripped) is
        // what must stay within the bound.
        var unwrapped = result.Content.Replace(SearchHighlighter.SentinelStart.ToString(), string.Empty).Replace(SearchHighlighter.SentinelEnd.ToString(), string.Empty);
        Assert.IsTrue(unwrapped.Length <= 200);
        Assert.IsTrue(result.Content.Contains($"{SearchHighlighter.SentinelStart}elephant{SearchHighlighter.SentinelEnd}"));
        // Roughly centered — the match shouldn't sit at the very start or end of the excerpt.
        var matchIndex = result.Content.IndexOf(SearchHighlighter.SentinelStart);
        Assert.IsTrue(matchIndex > 50 && matchIndex < 150, $"expected match roughly centered, was at index {matchIndex}");
    }

    [TestMethod]
    public void Build_ContentShorterThanExcerptLength_ReturnsWholeContent()
    {
        var content = "A short note mentioning elephant once.";

        var result = SearchHighlighter.Build("Title", content, new[] { "elephant" });

        var expected = $"A short note mentioning {SearchHighlighter.SentinelStart}elephant{SearchHighlighter.SentinelEnd} once.";
        Assert.AreEqual(expected, result.Content);
    }

    [TestMethod]
    public void Build_MultipleMatchingTerms_WrapsEachOne()
    {
        var result = SearchHighlighter.Build("Title", "elephant and safari together", new[] { "elephant", "safari" });

        var expected = $"{SearchHighlighter.SentinelStart}elephant{SearchHighlighter.SentinelEnd} and {SearchHighlighter.SentinelStart}safari{SearchHighlighter.SentinelEnd} together";
        Assert.AreEqual(expected, result.Content);
    }

    [TestMethod]
    public void Build_MarkupLikeContent_PassesThroughAsLiteralText()
    {
        var content = "<script>alert('elephant')</script> & more";

        var result = SearchHighlighter.Build("Title", content, new[] { "elephant" });

        // The angle brackets/ampersand pass through untouched as literal text — never
        // interpreted, never stripped, never turned into real HTML (SDS §44/§60).
        Assert.IsTrue(result.Content.Contains("<script>"));
        Assert.IsTrue(result.Content.Contains("</script>"));
        Assert.IsTrue(result.Content.Contains("&"));
        Assert.IsTrue(result.Content.Contains($"{SearchHighlighter.SentinelStart}elephant{SearchHighlighter.SentinelEnd}"));
    }

    [TestMethod]
    public void Build_NoContentMatch_ExcerptsFromStart()
    {
        // Only the title matched — content has no occurrence of any term, so the excerpt is
        // simply the start of the content, unwrapped.
        var filler = new string('x', 300);

        var result = SearchHighlighter.Build("Elephant Safari", filler, new[] { "elephant" });

        Assert.AreEqual(200, result.Content.Length);
        Assert.AreEqual(filler.Substring(0, 200), result.Content);
        Assert.IsFalse(result.Content.Contains(SearchHighlighter.SentinelStart));
    }
}
