using NoteManagement.Application.Services;

namespace NoteManagement.Tests.Unit.Application;

[TestClass]
public sealed class SearchTermTokenizerTests
{
    [TestMethod]
    public void Tokenize_SplitsOnWhitespace_ReturnsEachTerm()
    {
        var result = SearchTermTokenizer.Tokenize("elephant safari  adventure");

        CollectionAssert.AreEquivalent(new[] { "elephant", "safari", "adventure" }, result.ToArray());
    }

    [TestMethod]
    public void Tokenize_StripsFtsSpecialCharacters()
    {
        // '"', '(', ')', '*' carry special meaning inside a FORMSOF(...) predicate — stripped so
        // a term can never break out of its quoted phrase once SearchRepository wraps it.
        var result = SearchTermTokenizer.Tokenize("\"elephant\" (safari*)");

        CollectionAssert.AreEquivalent(new[] { "elephant", "safari" }, result.ToArray());
    }

    [TestMethod]
    public void Tokenize_DropsTermsThatSanitizeToEmpty()
    {
        var result = SearchTermTokenizer.Tokenize("elephant !!! safari");

        CollectionAssert.AreEquivalent(new[] { "elephant", "safari" }, result.ToArray());
    }

    [TestMethod]
    public void Tokenize_DeduplicatesCaseInsensitively()
    {
        var result = SearchTermTokenizer.Tokenize("Elephant elephant ELEPHANT");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Elephant", result[0]);
    }
}
