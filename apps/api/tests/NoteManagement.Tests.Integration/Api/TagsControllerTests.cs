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

namespace NoteManagement.Tests.Integration.Api;

[TestClass]
public sealed class TagsControllerTests
{
    private const string TestConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=NoteManagementDb_TagsControllerTests;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        // Isolated LocalDB database, distinct from every other test class's.
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
    public async Task Create_WithValidData_Returns201WithTag()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Post, "/api/tags", token, new { name = "Work", color = "#FF5733" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var tag = await response.Content.ReadFromJsonAsync<TagResponseDto>(JsonOptions);
        Assert.IsNotNull(tag);
        Assert.AreEqual("Work", tag.Name);
        Assert.AreEqual("#FF5733", tag.Color);
        Assert.AreEqual(0, tag.NoteCount);
        Assert.AreEqual(tag.CreatedAt, tag.UpdatedAt);
    }

    [TestMethod]
    public async Task Create_WithMissingOrBlankName_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var missingNameRequest = AuthedRequest(HttpMethod.Post, "/api/tags", token, new { color = "#FF5733" });
        var missingNameResponse = await _client.SendAsync(missingNameRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingNameResponse.StatusCode);

        using var blankNameRequest = AuthedRequest(HttpMethod.Post, "/api/tags", token, new { name = "   ", color = "#FF5733" });
        var blankNameResponse = await _client.SendAsync(blankNameRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, blankNameResponse.StatusCode);
    }

    [TestMethod]
    public async Task Create_WithNameOver50Chars_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tooLongName = new string('a', 51);

        using var request = AuthedRequest(HttpMethod.Post, "/api/tags", token, new { name = tooLongName, color = "#FF5733" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Create_WithMissingColor_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Post, "/api/tags", token, new { name = "Work" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Create_WithInvalidColorFormat_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();

        foreach (var invalidColor in new[] { "FF5733", "#FF57", "#GGGGGG", "red" })
        {
            using var request = AuthedRequest(HttpMethod.Post, "/api/tags", token, new { name = "Work", color = invalidColor });
            var response = await _client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"color '{invalidColor}' should be rejected");
        }
    }

    [TestMethod]
    public async Task Create_WithDuplicateName_Returns409()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateTagAsync(token, name: "Work");

        using var request = AuthedRequest(HttpMethod.Post, "/api/tags", token, new { name = "work", color = "#00FF00" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task Create_SameNameDifferentUsers_BothSucceed()
    {
        var tokenA = await CreateAuthenticatedUserAsync();
        var tokenB = await CreateAuthenticatedUserAsync();

        using var requestA = AuthedRequest(HttpMethod.Post, "/api/tags", tokenA, new { name = "Work", color = "#FF5733" });
        var responseA = await _client.SendAsync(requestA);
        using var requestB = AuthedRequest(HttpMethod.Post, "/api/tags", tokenB, new { name = "Work", color = "#00FF00" });
        var responseB = await _client.SendAsync(requestB);

        Assert.AreEqual(HttpStatusCode.Created, responseA.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, responseB.StatusCode);
    }

    [TestMethod]
    public async Task Create_WithoutAccessToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/tags", new { name = "Work", color = "#FF5733" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- List ----------

    [TestMethod]
    public async Task List_ReturnsOnlyCallersTags()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateTagAsync(token, name: "Work");
        await CreateTagAsync(token, name: "Personal");

        using var request = AuthedRequest(HttpMethod.Get, "/api/tags", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var tags = await response.Content.ReadFromJsonAsync<List<TagResponseDto>>(JsonOptions);
        Assert.IsNotNull(tags);
        Assert.AreEqual(2, tags.Count);
    }

    [TestMethod]
    public async Task List_NoteCountExcludesSoftDeletedNotes()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token, name: "Work");
        var activeNote = await CreateNoteAsync(token, tagIds: new[] { tag.Id });
        var deletedNote = await CreateNoteAsync(token, tagIds: new[] { tag.Id });
        using var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/notes/{deletedNote.Id}", token);
        await _client.SendAsync(deleteRequest);

        using var listRequest = AuthedRequest(HttpMethod.Get, "/api/tags", token);
        var response = await _client.SendAsync(listRequest);

        var tags = await response.Content.ReadFromJsonAsync<List<TagResponseDto>>(JsonOptions);
        Assert.IsNotNull(tags);
        Assert.AreEqual(1, tags.Single(t => t.Id == tag.Id).NoteCount);
    }

    [TestMethod]
    public async Task List_ExcludesOtherUsersTags()
    {
        var ownerToken = await CreateAuthenticatedUserAsync();
        await CreateTagAsync(ownerToken, name: "Owner's Tag");
        var otherToken = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Get, "/api/tags", otherToken);
        var response = await _client.SendAsync(request);

        var tags = await response.Content.ReadFromJsonAsync<List<TagResponseDto>>(JsonOptions);
        Assert.IsNotNull(tags);
        Assert.AreEqual(0, tags.Count);
    }

    [TestMethod]
    public async Task List_WithoutAccessToken_Returns401()
    {
        var response = await _client.GetAsync("/api/tags");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Update ----------

    [TestMethod]
    public async Task Update_WithValidData_Returns200()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token, name: "Old Name");

        using var request = AuthedRequest(HttpMethod.Put, $"/api/tags/{tag.Id}", token, new { name = "New Name", color = "#123456" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TagResponseDto>(JsonOptions);
        Assert.IsNotNull(updated);
        Assert.AreEqual("New Name", updated.Name);
        Assert.AreEqual("#123456", updated.Color);
    }

    [TestMethod]
    public async Task Update_WithSameNameNewColor_Returns200()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token, name: "Work", color: "#FF5733");

        using var request = AuthedRequest(HttpMethod.Put, $"/api/tags/{tag.Id}", token, new { name = "Work", color = "#000000" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TagResponseDto>(JsonOptions);
        Assert.IsNotNull(updated);
        Assert.AreEqual("#000000", updated.Color);
    }

    [TestMethod]
    public async Task Update_WithInvalidNameOrColor_Returns400()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token);

        using var badNameRequest = AuthedRequest(HttpMethod.Put, $"/api/tags/{tag.Id}", token, new { name = "", color = "#FF5733" });
        var badNameResponse = await _client.SendAsync(badNameRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, badNameResponse.StatusCode);

        using var badColorRequest = AuthedRequest(HttpMethod.Put, $"/api/tags/{tag.Id}", token, new { name = "Work", color = "blue" });
        var badColorResponse = await _client.SendAsync(badColorRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, badColorResponse.StatusCode);
    }

    [TestMethod]
    public async Task Update_WithDuplicateName_Returns409()
    {
        var token = await CreateAuthenticatedUserAsync();
        await CreateTagAsync(token, name: "Work");
        var other = await CreateTagAsync(token, name: "Personal");

        using var request = AuthedRequest(HttpMethod.Put, $"/api/tags/{other.Id}", token, new { name = "work", color = "#000000" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task Update_OtherUsersTag_Returns404()
    {
        var ownerToken = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(ownerToken);
        var otherToken = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Put, $"/api/tags/{tag.Id}", otherToken, new { name = "Hijacked", color = "#000000" });
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Delete ----------

    [TestMethod]
    public async Task Delete_WithOwnedTag_Returns204()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token);

        using var request = AuthedRequest(HttpMethod.Delete, $"/api/tags/{tag.Id}", token);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task Delete_PreservesAssociatedNotesButRemovesAssociation()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token);
        var note = await CreateNoteAsync(token, tagIds: new[] { tag.Id });

        using var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/tags/{tag.Id}", token);
        await _client.SendAsync(deleteRequest);

        using var getRequest = AuthedRequest(HttpMethod.Get, $"/api/notes/{note.Id}", token);
        var getResponse = await _client.SendAsync(getRequest);

        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<NoteResponseDto>(JsonOptions);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(0, fetched.Tags.Count);
    }

    [TestMethod]
    public async Task Delete_ThenList_ExcludesDeletedTag()
    {
        var token = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(token);

        using var deleteRequest = AuthedRequest(HttpMethod.Delete, $"/api/tags/{tag.Id}", token);
        await _client.SendAsync(deleteRequest);

        using var listRequest = AuthedRequest(HttpMethod.Get, "/api/tags", token);
        var listResponse = await _client.SendAsync(listRequest);

        var tags = await listResponse.Content.ReadFromJsonAsync<List<TagResponseDto>>(JsonOptions);
        Assert.IsNotNull(tags);
        Assert.IsFalse(tags.Any(t => t.Id == tag.Id));
    }

    [TestMethod]
    public async Task Delete_OtherUsersTag_Returns404()
    {
        var ownerToken = await CreateAuthenticatedUserAsync();
        var tag = await CreateTagAsync(ownerToken);
        var otherToken = await CreateAuthenticatedUserAsync();

        using var request = AuthedRequest(HttpMethod.Delete, $"/api/tags/{tag.Id}", otherToken);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        using var stillThereRequest = AuthedRequest(HttpMethod.Get, "/api/tags", ownerToken);
        var stillThereResponse = await _client.SendAsync(stillThereRequest);
        var tags = await stillThereResponse.Content.ReadFromJsonAsync<List<TagResponseDto>>(JsonOptions);
        Assert.IsNotNull(tags);
        Assert.IsTrue(tags.Any(t => t.Id == tag.Id));
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

    private static async Task<TagResponseDto> CreateTagAsync(string token, string name = "Work", string color = "#FF5733")
    {
        using var request = AuthedRequest(HttpMethod.Post, "/api/tags", token, new { name, color });
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var tag = await response.Content.ReadFromJsonAsync<TagResponseDto>(JsonOptions);
        Assert.IsNotNull(tag);
        return tag;
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
}
