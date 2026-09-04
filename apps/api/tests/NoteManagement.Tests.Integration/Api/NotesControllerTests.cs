using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoteManagement.Application.DTOs.Auth;
using NoteManagement.Application.DTOs.Notes;
using NoteManagement.Application.DTOs.Tags;
using NoteManagement.Infrastructure.Data;
using NoteManagement.Tests.Integration.TestSupport;

namespace NoteManagement.Tests.Integration.Api;

[TestClass]
public sealed class NotesControllerTests
{
    private static readonly string TestConnectionString =
        TestConnectionStringFactory.ForDatabase("NoteManagementDb_NotesControllerTests");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        // Isolated SQL Server Express database, distinct from every other test class's.
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

    // ---------- Create ----------

    [TestMethod]
    public async Task Create_WithValidData_Returns201WithNote()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { title = "My First Note", content = "Hello world" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var note = await response.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(note);
        Assert.AreEqual("My First Note", note.Title);
        Assert.AreEqual("Hello world", note.Content);
        Assert.AreEqual(note.CreatedAt, note.UpdatedAt);
        Assert.AreEqual(0, note.Tags.Count); // No tagIds submitted — the untagged path must not leak tags (AB-1006).
    }

    [TestMethod]
    public async Task Create_WithTagIds_Returns201WithTags()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token);

        using var request = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { title = "Tagged Note", content = "Hello", tagIds = new[] { tag.Id } });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var note = await response.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(note);
        Assert.AreEqual(1, note.Tags.Count);
        Assert.AreEqual(tag.Id, note.Tags[0].Id);
    }

    [TestMethod]
    public async Task Create_WithInvalidTagId_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { title = "Title", content = "Content", tagIds = new[] { Guid.NewGuid() } });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Create_WithMissingOrBlankTitle_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var missingTitleRequest = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { content = "x" });
        var missingTitleResponse = await _client.SendAsync(missingTitleRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingTitleResponse.StatusCode);

        using var blankTitleRequest = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { title = "   ", content = "x" });
        var blankTitleResponse = await _client.SendAsync(blankTitleRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, blankTitleResponse.StatusCode);
    }

    [TestMethod]
    public async Task Create_WithTitleOver200Chars_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tooLongTitle = new string('a', 201);

        using var request = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { title = tooLongTitle, content = "x" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Create_WithMissingOrBlankContent_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var missingContentRequest = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { title = "Title" });
        var missingContentResponse = await _client.SendAsync(missingContentRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingContentResponse.StatusCode);

        using var blankContentRequest = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { title = "Title", content = "   " });
        var blankContentResponse = await _client.SendAsync(blankContentRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, blankContentResponse.StatusCode);
    }

    [TestMethod]
    public async Task Create_WithoutAccessToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/notes", new { title = "Title", content = "Content" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- GetById ----------

    [TestMethod]
    public async Task GetById_WithOwnedNote_Returns200()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);

        using var request = AuthedRequest(HttpMethod.Get, $"/api/notes/{note.Id}", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(note.Id, fetched.Id);
        Assert.AreEqual(0, fetched.Tags.Count); // No tags assigned — the untagged path must not leak tags (AB-1006).
    }

    [TestMethod]
    public async Task GetById_WithUnknownId_Returns404()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Get, $"/api/notes/{Guid.NewGuid()}", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetById_WithAnotherUsersNote_Returns404()
    {
        var ownerToken = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(ownerToken);
        var otherUserToken = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Get, $"/api/notes/{note.Id}", otherUserToken);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetById_AfterSoftDelete_Returns404()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);
        using (var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token))
        {
            await _client.SendAsync(deleteRequest);
        }

        using var getRequest = AuthedRequest(HttpMethod.Get, $"/api/notes/{note.Id}", token);
        var response = await _client.SendAsync(getRequest);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- List ----------

    [TestMethod]
    public async Task List_ReturnsOwnedNotesWithPaginationEnvelope()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token);

        using var request = AuthedRequest(HttpMethod.Get, "/api/notes", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        Assert.AreEqual(1, list.Items.Count);
        Assert.AreEqual(1, list.Page);
        Assert.AreEqual(20, list.PageSize);
        Assert.AreEqual(1, list.TotalCount);
        Assert.AreEqual(1, list.TotalPages);
        Assert.AreEqual(0, list.Items[0].Tags.Count); // No tags assigned — the untagged path must not leak tags (AB-1006).
    }

    [TestMethod]
    public async Task List_ExcludesSoftDeletedNotes()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);
        using (var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token))
        {
            await _client.SendAsync(deleteRequest);
        }

        using var listRequest = AuthedRequest(HttpMethod.Get, "/api/notes", token);
        var response = await _client.SendAsync(listRequest);

        var list = await response.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        Assert.AreEqual(0, list.TotalCount);
        Assert.IsFalse(list.Items.Any(i => i.Id == note.Id));
    }

    [TestMethod]
    public async Task List_ExcludesOtherUsersNotes()
    {
        var ownerToken = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(ownerToken);
        var otherUserToken = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Get, "/api/notes", otherUserToken);
        var response = await _client.SendAsync(request);

        var list = await response.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        Assert.AreEqual(0, list.TotalCount);
    }

    [TestMethod]
    public async Task List_WithPageAndPageSize_ReturnsRequestedPage()
    {
        var token = await CreateAuthenticatedUserAsync();
        for (var i = 0; i < 7; i++)
        {
            await CreateNoteAsync(token, title: $"Note {i}");
        }

        using var request = AuthedRequest(HttpMethod.Get, "/api/notes?page=2&pageSize=3", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        Assert.AreEqual(3, list.Items.Count);
        Assert.AreEqual(2, list.Page);
        Assert.AreEqual(3, list.PageSize);
        Assert.AreEqual(7, list.TotalCount);
        Assert.AreEqual(3, list.TotalPages);
    }

    [TestMethod]
    public async Task List_WithSortByTitleAscending_ReturnsNotesOrderedByTitle()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token, title: "Bravo");
        await CreateNoteAsync(token, title: "Alpha");
        await CreateNoteAsync(token, title: "Charlie");

        using var request = AuthedRequest(HttpMethod.Get, "/api/notes?sortBy=title&sortDirection=asc", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        CollectionAssert.AreEqual(new[] { "Alpha", "Bravo", "Charlie" }, list.Items.Select(i => i.Title).ToArray());
    }

    [TestMethod]
    public async Task List_WithPageBeyondLastPage_ReturnsEmptyItemsNotError()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token);

        using var request = AuthedRequest(HttpMethod.Get, "/api/notes?page=999", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        Assert.AreEqual(0, list.Items.Count);
        Assert.AreEqual(1, list.TotalCount);
        Assert.AreEqual(1, list.TotalPages);
    }

    [TestMethod]
    public async Task List_WithInvalidPage_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        foreach (var page in new[] { "0", "-1", "abc" })
        {
            using var request = AuthedRequest(HttpMethod.Get, $"/api/notes?page={page}", token);
            var response = await _client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"page={page} should be rejected");
        }
    }

    [TestMethod]
    public async Task List_WithInvalidPageSize_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        foreach (var pageSize in new[] { "0", "-1", "abc" })
        {
            using var request = AuthedRequest(HttpMethod.Get, $"/api/notes?pageSize={pageSize}", token);
            var response = await _client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"pageSize={pageSize} should be rejected");
        }
    }

    [TestMethod]
    public async Task List_WithPageSizeOver100_ClampsTo100()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateNoteAsync(token);

        using var request = AuthedRequest(HttpMethod.Get, "/api/notes?pageSize=500", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        Assert.AreEqual(100, list.PageSize);
    }

    [TestMethod]
    public async Task List_WithUnsupportedSortBy_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Get, "/api/notes?sortBy=deletedAt", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task List_WithUnsupportedSortDirection_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Get, "/api/notes?sortDirection=sideways", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task List_WithTagIdFilter_ReturnsFilteredNotes()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token);
        var tagged = await CreateNoteAsync(token, title: "Tagged", tagIds: new[] { tag.Id });
        await CreateNoteAsync(token, title: "Untagged");

        using var request = AuthedRequest(HttpMethod.Get, $"/api/notes?tagId={tag.Id}", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        Assert.AreEqual(1, list.Items.Count);
        Assert.AreEqual(tagged.Id, list.Items[0].Id);
    }

    [TestMethod]
    public async Task List_WithTagIdFilterNoMatches_ReturnsEmptyItems()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token);
        await CreateNoteAsync(token);

        using var request = AuthedRequest(HttpMethod.Get, $"/api/notes?tagId={tag.Id}", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        Assert.AreEqual(0, list.Items.Count);
        Assert.AreEqual(0, list.TotalCount);
    }

    [TestMethod]
    public async Task List_WithInvalidTagId_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Get, $"/api/notes?tagId={Guid.NewGuid()}", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Update ----------

    [TestMethod]
    public async Task Update_WithValidData_Returns200WithUpdatedNote()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token, title: "Old Title", content: "Old Content");

        using var request = AuthedRequest(HttpMethod.Put, $"/api/notes/{note.Id}", token, new { title = "New Title", content = "New Content" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(updated);
        Assert.AreEqual("New Title", updated.Title);
        Assert.AreEqual("New Content", updated.Content);
        Assert.AreEqual(note.CreatedAt, updated.CreatedAt);
        Assert.IsTrue(updated.UpdatedAt >= note.UpdatedAt);
        Assert.AreEqual(0, updated.Tags.Count); // No tagIds submitted — the untagged path must not leak tags (AB-1006).
    }

    [TestMethod]
    public async Task Update_WithTagIds_Returns200WithUpdatedTags()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token);
        var note = await CreateNoteAsync(token);

        using var request = AuthedRequest(HttpMethod.Put, $"/api/notes/{note.Id}", token, new { title = "Title", content = "Content", tagIds = new[] { tag.Id } });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(updated);
        Assert.AreEqual(1, updated.Tags.Count);
        Assert.AreEqual(tag.Id, updated.Tags[0].Id);
    }

    [TestMethod]
    public async Task Update_WithInvalidTagId_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);

        using var request = AuthedRequest(HttpMethod.Put, $"/api/notes/{note.Id}", token, new { title = "Title", content = "Content", tagIds = new[] { Guid.NewGuid() } });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Update_WithInvalidData_Returns400AndDoesNotModifyNote()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token, title: "Original", content: "Original content");

        using var request = AuthedRequest(HttpMethod.Put, $"/api/notes/{note.Id}", token, new { title = "   ", content = "Original content" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        using var getRequest = AuthedRequest(HttpMethod.Get, $"/api/notes/{note.Id}", token);
        var getResponse = await _client.SendAsync(getRequest);
        var unchanged = await getResponse.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(unchanged);
        Assert.AreEqual("Original", unchanged.Title);
    }

    [TestMethod]
    public async Task Update_WithUnknownId_Returns404()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Put, $"/api/notes/{Guid.NewGuid()}", token, new { title = "Title", content = "Content" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Update_WithAnotherUsersNote_Returns404()
    {
        var ownerToken = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(ownerToken);
        var otherUserToken = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Put, $"/api/notes/{note.Id}", otherUserToken, new { title = "Hacked", content = "Hacked" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Update_AfterSoftDelete_Returns404()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);
        using (var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token))
        {
            await _client.SendAsync(deleteRequest);
        }

        using var updateRequest = AuthedRequest(HttpMethod.Put, $"/api/notes/{note.Id}", token, new { title = "New", content = "New" });
        var response = await _client.SendAsync(updateRequest);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Delete ----------

    [TestMethod]
    public async Task Delete_WithOwnedNote_Returns204()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);

        using var request = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task Delete_ThenGetById_Returns404()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);
        using (var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token))
        {
            await _client.SendAsync(deleteRequest);
        }

        using var getRequest = AuthedRequest(HttpMethod.Get, $"/api/notes/{note.Id}", token);
        var response = await _client.SendAsync(getRequest);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Delete_WithUnknownId_Returns404()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Delete, $"/api/notes/{Guid.NewGuid()}", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Delete_WithAnotherUsersNote_Returns404AndNoteRemainsActive()
    {
        var ownerToken = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(ownerToken);
        var otherUserToken = await CreateAuthenticatedUserAsync();

        using var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", otherUserToken);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.AreEqual(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        using var getRequest = AuthedRequest(HttpMethod.Get, $"/api/notes/{note.Id}", ownerToken);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [TestMethod]
    public async Task Delete_CalledTwice_SecondCallReturns404()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);
        using (var firstDeleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token))
        {
            var firstResponse = await _client.SendAsync(firstDeleteRequest);
            Assert.AreEqual(HttpStatusCode.NoContent, firstResponse.StatusCode);
        }

        using var secondDeleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token);
        var secondResponse = await _client.SendAsync(secondDeleteRequest);

        Assert.AreEqual(HttpStatusCode.NotFound, secondResponse.StatusCode);
    }

    // ---------- Restore ----------

    [TestMethod]
    public async Task Restore_WithSoftDeletedNote_Returns200()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);
        using (var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token))
        {
            await _client.SendAsync(deleteRequest);
        }

        using var restoreRequest = AuthedRequest(HttpMethod.Post, $"/api/notes/{note.Id}/restore", token);
        var response = await _client.SendAsync(restoreRequest);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var restored = await response.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(restored);
        Assert.AreEqual(note.Id, restored.Id);
        Assert.AreEqual(0, restored.Tags.Count); // No tags assigned — the untagged path must not leak tags (AB-1006).
    }

    [TestMethod]
    public async Task Restore_ThenGetByIdAndList_NoteIsActive()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);
        using (var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", token))
        {
            await _client.SendAsync(deleteRequest);
        }
        using (var restoreRequest = AuthedRequest(HttpMethod.Post, $"/api/notes/{note.Id}/restore", token))
        {
            await _client.SendAsync(restoreRequest);
        }

        using var getRequest = AuthedRequest(HttpMethod.Get, $"/api/notes/{note.Id}", token);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);

        using var listRequest = AuthedRequest(HttpMethod.Get, "/api/notes", token);
        var listResponse = await _client.SendAsync(listRequest);
        var list = await listResponse.Content.ReadFromJsonAsync<NoteListResponseDto>(JsonOptions);
        Assert.IsNotNull(list);
        Assert.IsTrue(list.Items.Any(i => i.Id == note.Id));
    }

    [TestMethod]
    public async Task Restore_WithUnknownId_Returns404()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Post, $"/api/notes/{Guid.NewGuid()}/restore", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Restore_WithAnotherUsersNote_Returns404()
    {
        var ownerToken = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(ownerToken);
        using (var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{note.Id}", ownerToken))
        {
            await _client.SendAsync(deleteRequest);
        }
        var otherUserToken = await CreateAuthenticatedUserAsync();

        using var restoreRequest = AuthedRequest(HttpMethod.Post, $"/api/notes/{note.Id}/restore", otherUserToken);
        var response = await _client.SendAsync(restoreRequest);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Restore_WithActiveNote_Returns409()
    {
        var token = await CreateAuthenticatedUserAsync();
        var note = await CreateNoteAsync(token);

        using var request = AuthedRequest(HttpMethod.Post, $"/api/notes/{note.Id}/restore", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
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

    /// <summary>Registers + logs in a fresh user and returns their access token — used for both the primary caller and a second "other user" per test, same shape as AuthControllerTests' RegisterAsync/LoginAsync pair.</summary>
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

    private static async Task<NoteResponseDto> CreateNoteAsync(string token, string title = "Title", string content = "Content", IReadOnlyList<Guid>? tagIds = null)
    {
        using var request = AuthedRequest(HttpMethod.Post, "/api/notes", token, new { title, content, tagIds });
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var note = await response.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(note);
        return note;
    }

    private static async Task<TagResponseDto> CreateTagAsync(string token, string name = "Work", string color = "#FF5733")
    {
        using var request = AuthedRequest(HttpMethod.Post, "/api/tags", token, new { name, color });
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var tag = await response.Content.ReadFromJsonAsync<TagResponseDto>(JsonOptions);
        Assert.IsNotNull(tag);
        return tag;
    }
}
