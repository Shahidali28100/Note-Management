using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoteManagement.Application.DTOs.Auth;
using NoteManagement.Application.DTOs.Notes;
using NoteManagement.Application.DTOs.Search;
using NoteManagement.Application.Services;
using NoteManagement.Infrastructure.Data;
using NoteManagement.Tests.Integration.TestSupport;

namespace NoteManagement.Tests.Integration.Api;

/// <summary>
/// Real full-text index/ranking/isolation behavior against a real SQL Server Express database —
/// same precedent as NotesControllerTests/TagsControllerTests. Every test that creates/updates a
/// note and immediately searches for it goes through WaitForFullTextIndexAsync, since
/// CHANGE_TRACKING AUTO population is asynchronous (plan.md architecture decisions).
/// </summary>
[TestClass]
public sealed class SearchControllerTests
{
    private static readonly string TestConnectionString =
        TestConnectionStringFactory.ForDatabase("NoteManagementDb_SearchControllerTests");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        // Isolated SQL Server Express database, distinct from every other test class's. The
        // AddNotesFullTextSearch migration creates the catalog/index as part of Database.Migrate()
        // below, same as every other schema change.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:DefaultConnection", TestConnectionString));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.Migrate();

        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ---------- Matching ----------

    [TestMethod]
    public async Task Search_WithSingleTermMatchingTitleOrContent_Returns200WithMatches()
    {
        var token = await CreateAuthenticatedUserAsync();
        var titleMatch = await CreateNoteAsync(token, title: "Elephant Safari", content: "A trip abroad.");
        var contentMatch = await CreateNoteAsync(token, title: "Trip Notes", content: "We saw an elephant today.");
        await CreateNoteAsync(token, title: "Unrelated", content: "Nothing to do with the search term.");

        var result = await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 2);

        Assert.AreEqual(2, result.TotalCount);
        CollectionAssert.AreEquivalent(new[] { titleMatch.Id, contentMatch.Id }, result.Items.Select(i => i.Id).ToArray());
    }

    [TestMethod]
    public async Task Search_WithMultiTermQuery_RequiresAllTerms()
    {
        var token = await CreateAuthenticatedUserAsync();
        var bothTerms = await CreateNoteAsync(token, title: "Cooking", content: "An elephant themed party for the kids.");
        var onlyOneTerm = await CreateNoteAsync(token, title: "Only Elephant", content: "This note mentions elephant but nothing else relevant.");
        await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 2);

        var result = await WaitForFullTextIndexAsync(token, "q=elephant+party", r => r.TotalCount == 1);

        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual(bothTerms.Id, result.Items[0].Id);
        Assert.IsFalse(result.Items.Any(i => i.Id == onlyOneTerm.Id));
    }

    [TestMethod]
    public async Task Search_WithNoMatches_Returns200WithEmptyItems()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token, title: "Unrelated", content: "Nothing matches here.");

        using var request = AuthedRequest(HttpMethod.Get, "/api/search?q=zzznomatchzzz", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchResponseDto>(JsonOptions);
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, result.TotalCount);
    }

    // ---------- Validation ----------

    [TestMethod]
    public async Task Search_WithMissingQ_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Get, "/api/search", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Search_WithBlankQ_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Get, "/api/search?q=%20%20%20", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Search_WithQOver200Chars_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tooLongQ = new string('a', 201);

        using var request = AuthedRequest(HttpMethod.Get, $"/api/search?q={tooLongQ}", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Search_WithoutAccessToken_Returns401()
    {
        var response = await _client.GetAsync("/api/search?q=elephant");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- User isolation ----------

    [TestMethod]
    public async Task Search_ExcludesOtherUsersMatchingNotes()
    {
        var ownerToken = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(ownerToken, title: "Elephant Zoo", content: "Owner's note.");
        var otherToken = await CreateAuthenticatedUserAsync();

        var result = await WaitForFullTextIndexAsync(otherToken, "q=elephant", r => true, maxAttempts: 5);

        Assert.AreEqual(0, result.TotalCount);
    }

    [TestMethod]
    public async Task Search_ExcludesSoftDeletedMatchingNotes()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token, title: "Elephant Note", content: "To be deleted.");
        await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 1);

        using (var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token))
        {
            await _client.SendAsync(deleteRequest);
        }

        var result = await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 0);

        Assert.AreEqual(0, result.TotalCount);
        Assert.IsFalse(result.Items.Any(i => i.Id == note.Id));
    }

    // ---------- Highlighting ----------

    [TestMethod]
    public async Task Search_TitleMatch_HighlightTitleContainsSentinelDelimitedTerm()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token, title: "Elephant Safari", content: "Unrelated body text.");

        var result = await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 1);

        var highlightTitle = result.Items[0].Highlight.Title;
        Assert.IsTrue(highlightTitle.Contains(SearchHighlighter.SentinelStart));
        Assert.IsTrue(highlightTitle.Contains(SearchHighlighter.SentinelEnd));
        Assert.IsFalse(highlightTitle.Contains('<'));
    }

    [TestMethod]
    public async Task Search_ContentMatch_HighlightContentIsBoundedExcerptWithSentinels()
    {
        var token = await CreateAuthenticatedUserAsync();
        var filler = new string('x', 300);
        await CreateNoteAsync(token, title: "Trip Notes", content: filler + " elephant " + filler);

        var result = await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 1);

        var highlightContent = result.Items[0].Highlight.Content;
        Assert.IsTrue(highlightContent.Contains(SearchHighlighter.SentinelStart));
        var unwrapped = highlightContent.Replace(SearchHighlighter.SentinelStart.ToString(), string.Empty).Replace(SearchHighlighter.SentinelEnd.ToString(), string.Empty);
        Assert.IsTrue(unwrapped.Length <= 200);
    }

    [TestMethod]
    public async Task Search_MultiTermMatch_HighlightsEveryTerm()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token, title: "Trip", content: "An elephant and a safari together.");

        var result = await WaitForFullTextIndexAsync(token, "q=elephant+safari", r => r.TotalCount == 1);

        var highlightContent = result.Items[0].Highlight.Content;
        var occurrences = highlightContent.Split(SearchHighlighter.SentinelStart).Length - 1;
        Assert.AreEqual(2, occurrences); // both "elephant" and "safari" individually wrapped
    }

    [TestMethod]
    public async Task Search_NoteContentWithAngleBrackets_HighlightNeverContainsHtml()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token, title: "Trip", content: "<script>alert('elephant')</script> markup-like text.");

        var result = await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 1);

        var highlightContent = result.Items[0].Highlight.Content;
        // The angle brackets pass through as literal text alongside the sentinel markers — never
        // interpreted, and no HTML of the highlighter's own construction is introduced (SDS §44/§60).
        Assert.IsTrue(highlightContent.Contains("<script>"));
        Assert.IsTrue(highlightContent.Contains(SearchHighlighter.SentinelStart));
    }

    // ---------- Pagination ----------

    [TestMethod]
    public async Task Search_WithNoPagingParams_UsesPage1PageSize20()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token, title: "Elephant Note", content: "Content.");

        var result = await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 1);

        Assert.AreEqual(1, result.Page);
        Assert.AreEqual(20, result.PageSize);
    }

    [TestMethod]
    public async Task Search_WithPageAndPageSize_ReturnsRequestedSlice()
    {
        var token = await CreateAuthenticatedUserAsync();
        for (var i = 0; i < 7; i++)
        {
            await CreateNoteAsync(token, title: $"Elephant Note {i}", content: "Content.");
        }

        var result = await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 7);
        var page = await WaitForFullTextIndexAsync(token, "q=elephant&page=2&pageSize=3", r => r.Items.Count == 3);

        Assert.AreEqual(3, page.Items.Count);
        Assert.AreEqual(2, page.Page);
        Assert.AreEqual(3, page.PageSize);
        Assert.AreEqual(7, page.TotalCount);
        Assert.AreEqual(3, page.TotalPages);
    }

    [TestMethod]
    public async Task Search_WithInvalidPage_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        foreach (var page in new[] { "0", "-1", "abc" })
        {
            using var request = AuthedRequest(HttpMethod.Get, $"/api/search?q=elephant&page={page}", token);
            var response = await _client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"page={page} should be rejected");
        }
    }

    [TestMethod]
    public async Task Search_WithInvalidPageSize_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        foreach (var pageSize in new[] { "0", "-1", "abc" })
        {
            using var request = AuthedRequest(HttpMethod.Get, $"/api/search?q=elephant&pageSize={pageSize}", token);
            var response = await _client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"pageSize={pageSize} should be rejected");
        }
    }

    [TestMethod]
    public async Task Search_WithPageSizeOver100_ClampsTo100()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token, title: "Elephant Note", content: "Content.");

        var result = await WaitForFullTextIndexAsync(token, "q=elephant&pageSize=500", r => r.TotalCount == 1);

        Assert.AreEqual(100, result.PageSize);
    }

    [TestMethod]
    public async Task Search_WithPageBeyondLastPage_ReturnsEmptyItems()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token, title: "Elephant Note", content: "Content.");
        await WaitForFullTextIndexAsync(token, "q=elephant", r => r.TotalCount == 1);

        using var request = AuthedRequest(HttpMethod.Get, "/api/search?q=elephant&page=999", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchResponseDto>(JsonOptions);
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(1, result.TotalCount);
    }

    // ---------- Helpers ----------

    private static string UniqueEmail([System.Runtime.CompilerServices.CallerMemberName] string? testName = null) =>
        $"{testName}_{Guid.NewGuid():N}@example.com";

    private static async Task RegisterAsync(string email, string password = "Passw0rd", string name = "Test User")
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { name, email, password });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<AuthTokensDto> LoginAsync(string email, string password = "Passw0rd")
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokensDto>(JsonOptions);
        Assert.IsNotNull(tokens);
        return tokens;
    }

    private static async Task<string> CreateAuthenticatedUserAsync()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var tokens = await LoginAsync(email);
        return tokens.AccessToken;
    }

    private static HttpRequestMessage AuthedRequest(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<NoteResponseDto> CreateNoteAsync(string token, string title = "Title", string content = "Content")
    {
        using var request = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { title, content });
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var note = await response.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(note);
        return note;
    }

    /// <summary>
    /// Polls GET /api/search?{queryString} until isReady(result) is true or maxAttempts is
    /// exhausted, then returns the last observed (possibly not-yet-ready) result. Necessary
    /// because the full-text index's CHANGE_TRACKING AUTO population is asynchronous — a note
    /// created in the immediately preceding statement can race the background index population
    /// (plan.md architecture decisions). A test whose isReady predicate never becomes true still
    /// gets a clear assertion failure from its own Assert calls on the returned (stale) result,
    /// rather than this helper throwing an opaque timeout.
    /// </summary>
    private static async Task<SearchResponseDto> WaitForFullTextIndexAsync(string token, string queryString, Func<SearchResponseDto, bool> isReady, int maxAttempts = 20, int delayMs = 500)
    {
        SearchResponseDto? last = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var request = AuthedRequest(HttpMethod.Get, $"/api/search?{queryString}", token);
            var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                last = await response.Content.ReadFromJsonAsync<SearchResponseDto>(JsonOptions);
                if (last is not null && isReady(last))
                {
                    return last;
                }
            }

            await Task.Delay(delayMs);
        }

        return last ?? new SearchResponseDto(Array.Empty<SearchResultDto>(), 1, 20, 0, 0);
    }
}
