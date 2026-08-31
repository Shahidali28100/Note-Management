using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NoteManagement.Application.DTOs.Auth;
using NoteManagement.Application.Interfaces;
using NoteManagement.Infrastructure.Data;

namespace NoteManagement.Tests.Integration.Api;

[TestClass]
public sealed class AuthControllerTests
{
    private const string TestConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=NoteManagementDb_AuthControllerTests;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        // Isolated LocalDB database, distinct from HealthEndpointTests' and the manual/dev database.
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

    // ---------- Register ----------

    [TestMethod]
    public async Task Register_WithValidData_Returns201WithUser()
    {
        var email = UniqueEmail();

        var response = await _client.PostAsJsonAsync("/api/auth/register", new { name = "Alice", email, password = "Passw0rd" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.IsNotNull(user);
        Assert.AreEqual(email, user.Email);
    }

    [TestMethod]
    public async Task Register_WithDuplicateEmail_Returns409()
    {
        var email = UniqueEmail();
        await _client.PostAsJsonAsync("/api/auth/register", new { name = "Alice", email, password = "Passw0rd" });

        var response = await _client.PostAsJsonAsync("/api/auth/register", new { name = "Alice", email, password = "Passw0rd" });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task Register_WithInvalidEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { name = "Alice", email = "not-an-email", password = "Passw0rd" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Register_WithWeakPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { name = "Alice", email = UniqueEmail(), password = "short" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Register_WithMissingRequiredField_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { email = UniqueEmail(), password = "Passw0rd" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Login ----------

    [TestMethod]
    public async Task Login_WithValidCredentials_Returns200WithTokens()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokensDto>(JsonOptions);
        Assert.IsNotNull(tokens);
        Assert.IsFalse(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.IsFalse(string.IsNullOrEmpty(tokens.RefreshToken));
    }

    [TestMethod]
    public async Task Login_WithIncorrectPassword_Returns401()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPassword1" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = UniqueEmail(), password = "Passw0rd" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_CalledTwiceForSameUser_BothRefreshTokensRemainValid()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var firstSession = await LoginAsync(email);
        var secondSession = await LoginAsync(email);

        var firstRefresh = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = firstSession.RefreshToken });
        var secondRefresh = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = secondSession.RefreshToken });

        Assert.AreEqual(HttpStatusCode.OK, firstRefresh.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondRefresh.StatusCode);
    }

    // ---------- GetMe ----------

    [TestMethod]
    public async Task GetMe_WithValidToken_Returns200WithUserProfile()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var tokens = await LoginAsync(email);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.IsNotNull(user);
        Assert.AreEqual(email, user.Email);
    }

    [TestMethod]
    public async Task GetMe_WithExpiredToken_Returns401()
    {
        var expiredToken = BuildAccessToken(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMe_WithTamperedToken_Returns401()
    {
        var tamperedToken = BuildAccessToken(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(15), signingKeyOverride: "a-deliberately-wrong-signing-key-0123456789");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMe_WithNoToken_Returns401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Refresh ----------

    [TestMethod]
    public async Task Refresh_WithValidToken_Returns200AndOldTokenNoLongerWorks()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var tokens = await LoginAsync(email);

        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.AreEqual(HttpStatusCode.OK, refreshResponse.StatusCode);

        var reuseResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.AreEqual(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [TestMethod]
    public async Task Refresh_WithExpiredToken_Returns401()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var tokens = await LoginAsync(email);

        // No way to manufacture a naturally-7-day-expired token through the public API —
        // backdate it directly via the DbContext (test fixture setup, not production code).
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var secretService = scope.ServiceProvider.GetRequiredService<IRefreshTokenSecretService>();
            var tokenHash = secretService.Hash(tokens.RefreshToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE RefreshTokens SET ExpiresAt = {DateTime.UtcNow.AddDays(-1)} WHERE TokenHash = {tokenHash}");
        }

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Refresh_WithUnknownToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "not-a-real-token" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Refresh_WithReusedRotatedToken_Returns401AndInvalidatesOtherActiveSession()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var firstSession = await LoginAsync(email);
        var secondSession = await LoginAsync(email);

        // Rotate the first session's token, then present the now-revoked original again.
        await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = firstSession.RefreshToken });
        var reuseResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = firstSession.RefreshToken });
        Assert.AreEqual(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        // The reuse-detection cascade should have revoked the second, independent session too.
        var secondSessionRefresh = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = secondSession.RefreshToken });
        Assert.AreEqual(HttpStatusCode.Unauthorized, secondSessionRefresh.StatusCode);
    }

    // ---------- Logout ----------

    [TestMethod]
    public async Task Logout_WithValidToken_Returns204()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var tokens = await LoginAsync(email);

        var response = await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = tokens.RefreshToken });

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task Logout_ThenRefreshWithSameToken_Returns401()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var tokens = await LoginAsync(email);
        await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = tokens.RefreshToken });

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Logout_WithOneOfTwoSessions_OtherSessionRemainsValid()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var firstSession = await LoginAsync(email);
        var secondSession = await LoginAsync(email);

        await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = firstSession.RefreshToken });
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = secondSession.RefreshToken });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
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

    /// <summary>
    /// Builds a raw JWT directly, independent of the production IJwtTokenGenerator — needed
    /// because the generator only ever issues a live, 15-minute token. Reads the real signing
    /// key/issuer/audience from the test host's own IConfiguration so it can never drift from
    /// what Program.cs validates against.
    /// </summary>
    private static string BuildAccessToken(Guid userId, DateTime expiresUtc, string? signingKeyOverride = null)
    {
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        var signingKey = signingKeyOverride ?? configuration["Jwt:SigningKey"]!;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            expires: expiresUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
