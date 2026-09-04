using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NoteManagement.Application.DTOs.Auth;
using NoteManagement.Application.Interfaces;
using NoteManagement.Infrastructure.Data;
using NoteManagement.Tests.Integration.TestSupport;

namespace NoteManagement.Tests.Integration.Api;

[TestClass]
public sealed class AuthControllerTests
{
    private static readonly string TestConnectionString =
        TestConnectionStringFactory.ForDatabase("NoteManagementDb_AuthControllerTests");

    // AB-1003: a separate factory/database/client whose IOtpGenerator is substituted with a
    // deterministic test double — the raw OTP is only ever logged, never returned via HTTP, so
    // any test that needs to complete a full reset (not just check forgot-password's response
    // shape) needs to know the code in advance. See SequentialOtpGenerator's remarks.
    private static readonly string OtpTestConnectionString =
        TestConnectionStringFactory.ForDatabase("NoteManagementDb_AuthControllerTests_Otp");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    private static WebApplicationFactory<Program> _otpFactory = null!;
    private static HttpClient _otpClient = null!;
    private static SequentialOtpGenerator _sequentialOtpGenerator = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        // Isolated SQL Server Express database, distinct from HealthEndpointTests' and the manual/dev database.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:DefaultConnection", TestConnectionString));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.Migrate();

        _client = _factory.CreateClient();

        _sequentialOtpGenerator = new SequentialOtpGenerator();
        _otpFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", OtpTestConnectionString);
            builder.ConfigureTestServices(services => services.AddSingleton<IOtpGenerator>(_sequentialOtpGenerator));
        });

        using var otpScope = _otpFactory.Services.CreateScope();
        var otpDbContext = otpScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        otpDbContext.Database.Migrate();

        _otpClient = _otpFactory.CreateClient();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _client.Dispose();
        _factory.Dispose();
        _otpClient.Dispose();
        _otpFactory.Dispose();
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

    // ---------- Forgot Password ----------

    [TestMethod]
    public async Task ForgotPassword_WithRegisteredEmail_Returns200()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ForgotPassword_WithUnknownAndRegisteredEmail_ReturnsIdenticalResponse()
    {
        var registeredEmail = UniqueEmail();
        await RegisterAsync(registeredEmail);
        var unknownEmail = UniqueEmail();

        var registeredResponse = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = registeredEmail });
        var unknownResponse = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = unknownEmail });

        Assert.AreEqual(registeredResponse.StatusCode, unknownResponse.StatusCode);
        var registeredBody = await registeredResponse.Content.ReadAsStringAsync();
        var unknownBody = await unknownResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(registeredBody, unknownBody);
    }

    [TestMethod]
    public async Task ForgotPassword_CalledTwiceQuickly_ReturnsSame200BothTimes()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var first = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var second = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
    }

    // ---------- Reset Password ----------
    // These use _otpClient/_otpFactory (deterministic IOtpGenerator) so the test can submit the
    // exact code it just had issued — see SequentialOtpGenerator's remarks.

    [TestMethod]
    public async Task ResetPassword_WithValidOtp_Returns200()
    {
        var email = UniqueEmail();
        await RegisterAsync(email, client: _otpClient);
        _sequentialOtpGenerator.Enqueue("111111");
        await _otpClient.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        var response = await _otpClient.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = "111111", newPassword = "NewPassw0rd1" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ResetPassword_WithWrongOtp_Returns400()
    {
        var email = UniqueEmail();
        await RegisterAsync(email, client: _otpClient);
        _sequentialOtpGenerator.Enqueue("222222");
        await _otpClient.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        var response = await _otpClient.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = "000000", newPassword = "NewPassw0rd1" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ResetPassword_WithSupersededOtp_Returns400()
    {
        var email = UniqueEmail();
        await RegisterAsync(email, client: _otpClient);
        _sequentialOtpGenerator.Enqueue("333333");
        await _otpClient.PostAsJsonAsync("/api/auth/forgot-password", new { email }); // OTP #1

        // No way to bypass the 60s reissue cooldown through the public API alone — backdate the
        // just-issued OTP's CreatedAt directly via the DbContext (test fixture setup, mirrors
        // Refresh_WithExpiredToken_Returns401's precedent below), so the next forgot-password
        // call is treated as outside the cooldown window and actually supersedes it.
        using (var scope = _otpFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE PasswordResetOtps SET CreatedAt = {DateTime.UtcNow.AddMinutes(-2)} WHERE UserId = (SELECT Id FROM Users WHERE Email = {email})");
        }

        _sequentialOtpGenerator.Enqueue("444444");
        await _otpClient.PostAsJsonAsync("/api/auth/forgot-password", new { email }); // OTP #2 supersedes #1

        var response = await _otpClient.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = "333333", newPassword = "NewPassw0rd1" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ResetPassword_CalledTwiceWithSameOtp_SecondCallReturns400()
    {
        var email = UniqueEmail();
        await RegisterAsync(email, client: _otpClient);
        _sequentialOtpGenerator.Enqueue("555555");
        await _otpClient.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        var first = await _otpClient.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = "555555", newPassword = "NewPassw0rd1" });
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        var second = await _otpClient.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = "555555", newPassword = "AnotherPass1" });
        Assert.AreEqual(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [TestMethod]
    public async Task ResetPassword_After5WrongAttempts_CorrectCodeSubsequentlyRejected()
    {
        var email = UniqueEmail();
        await RegisterAsync(email, client: _otpClient);
        _sequentialOtpGenerator.Enqueue("666666");
        await _otpClient.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        for (var i = 0; i < 5; i++)
        {
            var wrongResponse = await _otpClient.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = "000001", newPassword = "NewPassw0rd1" });
            Assert.AreEqual(HttpStatusCode.BadRequest, wrongResponse.StatusCode);
        }

        var correctResponse = await _otpClient.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = "666666", newPassword = "NewPassw0rd1" });

        Assert.AreEqual(HttpStatusCode.BadRequest, correctResponse.StatusCode);
    }

    [TestMethod]
    public async Task ResetPassword_WithWeakNewPassword_Returns400()
    {
        // DataAnnotations short-circuits before AuthService — same precedent as
        // Register_WithWeakPassword_Returns400 — so no real user/OTP is needed.
        var response = await _client.PostAsJsonAsync("/api/auth/reset-password", new { email = UniqueEmail(), otp = "123456", newPassword = "short" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ResetPassword_ThenRefreshWithOldToken_Returns401()
    {
        var email = UniqueEmail();
        await RegisterAsync(email, client: _otpClient);
        var oldTokens = await LoginAsync(email, client: _otpClient);
        _sequentialOtpGenerator.Enqueue("777777");
        await _otpClient.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        var resetResponse = await _otpClient.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = "777777", newPassword = "NewPassw0rd1" });
        Assert.AreEqual(HttpStatusCode.OK, resetResponse.StatusCode);

        var refreshResponse = await _otpClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldTokens.RefreshToken });

        Assert.AreEqual(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    // ---------- Helpers ----------

    private static string UniqueEmail([System.Runtime.CompilerServices.CallerMemberName] string? testName = null) =>
        $"{testName}_{Guid.NewGuid():N}@example.com";

    private static async Task RegisterAsync(string email, string password = "Passw0rd", string name = "Test User", HttpClient? client = null)
    {
        var response = await (client ?? _client).PostAsJsonAsync("/api/auth/register", new { name, email, password });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<AuthTokensDto> LoginAsync(string email, string password = "Passw0rd", HttpClient? client = null)
    {
        var response = await (client ?? _client).PostAsJsonAsync("/api/auth/login", new { email, password });
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

    /// <summary>
    /// Test double substituted for the real OtpGenerator in _otpFactory (see ClassInitialize) —
    /// the raw OTP is only ever logged, never returned via HTTP (FRS-AUTH-005), so a test that
    /// needs to complete a full reset must know the code in advance. Enqueue() lets each test
    /// pick its own code(s) before calling forgot-password. Hash only needs to be internally
    /// self-consistent (both issuance and verification route through this same singleton
    /// instance within _otpFactory) — it doesn't need to match the real algorithm, which
    /// OtpGeneratorTests already covers directly.
    /// </summary>
    private sealed class SequentialOtpGenerator : IOtpGenerator
    {
        private readonly Queue<string> _queue = new();

        public void Enqueue(string otp) => _queue.Enqueue(otp);

        public string GenerateRawOtp() =>
            _queue.Count > 0
                ? _queue.Dequeue()
                : throw new InvalidOperationException("SequentialOtpGenerator queue exhausted — Enqueue enough codes for this test.");

        public string Hash(string rawOtp) => $"otp-hash:{rawOtp}";
    }
}
