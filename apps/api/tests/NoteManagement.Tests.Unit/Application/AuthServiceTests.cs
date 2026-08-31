using NoteManagement.Application.DTOs.Auth;
using NoteManagement.Application.Exceptions;
using NoteManagement.Application.Interfaces;
using NoteManagement.Application.Services;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Tests.Unit.Application;

/// <summary>
/// Hand-rolled fakes for all 6 dependencies, matching HealthCheckServiceTests'
/// "no mocking library" convention — none of these interfaces are complex enough to
/// justify adding Moq/NSubstitute as a dependency.
/// </summary>
[TestClass]
public sealed class AuthServiceTests
{
    [TestMethod]
    public async Task RegisterAsync_WithValidData_CreatesUser()
    {
        var userRepository = new FakeUserRepository();
        var sut = CreateSut(userRepository: userRepository);
        var request = new RegisterRequestDto("Alice", "alice@example.com", "Passw0rd");

        var result = await sut.RegisterAsync(request, CancellationToken.None);

        Assert.AreEqual("Alice", result.Name);
        Assert.AreEqual("alice@example.com", result.Email);
        Assert.AreEqual(1, userRepository.Added.Count);
        Assert.AreNotEqual("Passw0rd", userRepository.Added[0].PasswordHash);
    }

    [TestMethod]
    public async Task RegisterAsync_WithDuplicateEmail_ThrowsDuplicateEmailException()
    {
        var unitOfWork = new FakeUnitOfWork { ThrowUniqueConstraintViolationOnNextSave = true };
        var sut = CreateSut(unitOfWork: unitOfWork);
        var request = new RegisterRequestDto("Alice", "alice@example.com", "Passw0rd");

        await Assert.ThrowsExactlyAsync<DuplicateEmailException>(() => sut.RegisterAsync(request, CancellationToken.None));
    }

    [TestMethod]
    public async Task LoginAsync_WithValidCredentials_ReturnsTokensAndPersistsHashedRefreshToken()
    {
        var passwordHasher = new FakePasswordHasher();
        var user = User.Register("Alice", "alice@example.com", passwordHasher.Hash("Passw0rd"));
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var refreshTokenRepository = new FakeRefreshTokenRepository();
        var sut = CreateSut(userRepository: userRepository, refreshTokenRepository: refreshTokenRepository, passwordHasher: passwordHasher);
        var request = new LoginRequestDto("alice@example.com", "Passw0rd");

        var result = await sut.LoginAsync(request, CancellationToken.None);

        Assert.IsFalse(string.IsNullOrEmpty(result.AccessToken));
        Assert.AreEqual(1, refreshTokenRepository.Added.Count);
        // The persisted value is a hash, never the raw token the caller receives.
        Assert.AreNotEqual(result.RefreshToken, refreshTokenRepository.Added[0].TokenHash);
    }

    [TestMethod]
    public async Task LoginAsync_WithIncorrectPassword_ThrowsInvalidCredentialsException()
    {
        var passwordHasher = new FakePasswordHasher();
        var user = User.Register("Alice", "alice@example.com", passwordHasher.Hash("Passw0rd"));
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var sut = CreateSut(userRepository: userRepository, passwordHasher: passwordHasher);
        var request = new LoginRequestDto("alice@example.com", "WrongPassword1");

        await Assert.ThrowsExactlyAsync<InvalidCredentialsException>(() => sut.LoginAsync(request, CancellationToken.None));
    }

    [TestMethod]
    public async Task LoginAsync_WithUnknownEmail_ThrowsInvalidCredentialsException()
    {
        var sut = CreateSut();
        var request = new LoginRequestDto("nobody@example.com", "Passw0rd");

        await Assert.ThrowsExactlyAsync<InvalidCredentialsException>(() => sut.LoginAsync(request, CancellationToken.None));
    }

    [TestMethod]
    public async Task RefreshAsync_WithValidToken_RotatesAndReturnsNewTokens()
    {
        var refreshTokenSecretService = new FakeRefreshTokenSecretService();
        var userId = Guid.NewGuid();
        const string rawToken = "raw-token-existing";
        var existingToken = RefreshToken.Issue(userId, refreshTokenSecretService.Hash(rawToken), DateTime.UtcNow.AddDays(7));
        var refreshTokenRepository = new FakeRefreshTokenRepository(existingToken);
        var sut = CreateSut(refreshTokenRepository: refreshTokenRepository, refreshTokenSecretService: refreshTokenSecretService);
        var request = new RefreshRequestDto(rawToken);

        var result = await sut.RefreshAsync(request, CancellationToken.None);

        Assert.IsNotNull(existingToken.RevokedAt);
        Assert.AreEqual(1, refreshTokenRepository.Added.Count);
        Assert.AreNotEqual(rawToken, result.RefreshToken);
    }

    [TestMethod]
    public async Task RefreshAsync_WithExpiredToken_ThrowsInvalidRefreshTokenException()
    {
        var refreshTokenSecretService = new FakeRefreshTokenSecretService();
        const string rawToken = "raw-token-expired";
        var existingToken = RefreshToken.Issue(Guid.NewGuid(), refreshTokenSecretService.Hash(rawToken), DateTime.UtcNow.AddSeconds(-1));
        var refreshTokenRepository = new FakeRefreshTokenRepository(existingToken);
        var sut = CreateSut(refreshTokenRepository: refreshTokenRepository, refreshTokenSecretService: refreshTokenSecretService);
        var request = new RefreshRequestDto(rawToken);

        await Assert.ThrowsExactlyAsync<InvalidRefreshTokenException>(() => sut.RefreshAsync(request, CancellationToken.None));
        Assert.AreEqual(0, refreshTokenRepository.RevokeAllActiveForUserCalls.Count);
    }

    [TestMethod]
    public async Task RefreshAsync_WithUnknownToken_ThrowsInvalidRefreshTokenException()
    {
        var sut = CreateSut();
        var request = new RefreshRequestDto("does-not-exist");

        await Assert.ThrowsExactlyAsync<InvalidRefreshTokenException>(() => sut.RefreshAsync(request, CancellationToken.None));
    }

    [TestMethod]
    public async Task RefreshAsync_WithAlreadyRevokedToken_RevokesAllActiveSessionsForUserAndThrows()
    {
        var refreshTokenSecretService = new FakeRefreshTokenSecretService();
        var userId = Guid.NewGuid();
        const string rawToken = "raw-token-reused";
        var revokedToken = RefreshToken.Issue(userId, refreshTokenSecretService.Hash(rawToken), DateTime.UtcNow.AddDays(7));
        revokedToken.Revoke();
        var refreshTokenRepository = new FakeRefreshTokenRepository(revokedToken);
        var sut = CreateSut(refreshTokenRepository: refreshTokenRepository, refreshTokenSecretService: refreshTokenSecretService);
        var request = new RefreshRequestDto(rawToken);

        await Assert.ThrowsExactlyAsync<InvalidRefreshTokenException>(() => sut.RefreshAsync(request, CancellationToken.None));
        CollectionAssert.Contains(refreshTokenRepository.RevokeAllActiveForUserCalls, userId);
    }

    [TestMethod]
    public async Task LogoutAsync_WithValidToken_RevokesToken()
    {
        var refreshTokenSecretService = new FakeRefreshTokenSecretService();
        const string rawToken = "raw-token-to-logout";
        var existingToken = RefreshToken.Issue(Guid.NewGuid(), refreshTokenSecretService.Hash(rawToken), DateTime.UtcNow.AddDays(7));
        var refreshTokenRepository = new FakeRefreshTokenRepository(existingToken);
        var sut = CreateSut(refreshTokenRepository: refreshTokenRepository, refreshTokenSecretService: refreshTokenSecretService);
        var request = new LogoutRequestDto(rawToken);

        await sut.LogoutAsync(request, CancellationToken.None);

        Assert.IsNotNull(existingToken.RevokedAt);
    }

    [TestMethod]
    public async Task LogoutAsync_WithUnknownToken_ThrowsInvalidRefreshTokenException()
    {
        var sut = CreateSut();
        var request = new LogoutRequestDto("does-not-exist");

        await Assert.ThrowsExactlyAsync<InvalidRefreshTokenException>(() => sut.LogoutAsync(request, CancellationToken.None));
    }

    private static AuthService CreateSut(
        FakeUserRepository? userRepository = null,
        FakeRefreshTokenRepository? refreshTokenRepository = null,
        FakeUnitOfWork? unitOfWork = null,
        FakePasswordHasher? passwordHasher = null,
        FakeJwtTokenGenerator? jwtTokenGenerator = null,
        FakeRefreshTokenSecretService? refreshTokenSecretService = null) =>
        new(
            userRepository ?? new FakeUserRepository(),
            refreshTokenRepository ?? new FakeRefreshTokenRepository(),
            unitOfWork ?? new FakeUnitOfWork(),
            passwordHasher ?? new FakePasswordHasher(),
            jwtTokenGenerator ?? new FakeJwtTokenGenerator(),
            refreshTokenSecretService ?? new FakeRefreshTokenSecretService());

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly Dictionary<string, User> _byEmail = new();

        public FakeUserRepository(User? existingByEmail = null)
        {
            if (existingByEmail is not null)
            {
                _byEmail[existingByEmail.Email] = existingByEmail;
            }
        }

        public List<User> Added { get; } = new();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(_byEmail.GetValueOrDefault(email));

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_byEmail.Values.FirstOrDefault(u => u.Id == id));

        public void Add(User user)
        {
            Added.Add(user);
            _byEmail[user.Email] = user;
        }
    }

    private sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly Dictionary<string, RefreshToken> _byHash;

        public FakeRefreshTokenRepository(params RefreshToken[] existing)
        {
            _byHash = existing.ToDictionary(t => t.TokenHash);
        }

        public List<RefreshToken> Added { get; } = new();

        public List<Guid> RevokeAllActiveForUserCalls { get; } = new();

        public void Add(RefreshToken token)
        {
            Added.Add(token);
            _byHash[token.TokenHash] = token;
        }

        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(_byHash.GetValueOrDefault(tokenHash));

        public Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            RevokeAllActiveForUserCalls.Add(userId);
            foreach (var token in _byHash.Values.Where(t => t.UserId == userId && t.IsActive))
            {
                token.Revoke();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public bool ThrowUniqueConstraintViolationOnNextSave { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (ThrowUniqueConstraintViolationOnNextSave)
            {
                ThrowUniqueConstraintViolationOnNextSave = false;
                throw new UniqueConstraintViolationException(new InvalidOperationException("simulated unique-index violation"));
            }

            return Task.CompletedTask;
        }

        public Task RunInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string password, string passwordHash) => passwordHash == Hash(password);
    }

    private sealed class FakeJwtTokenGenerator : IJwtTokenGenerator
    {
        public (string AccessToken, DateTime ExpiresAtUtc) GenerateAccessToken(Guid userId) =>
            ($"access-token-for-{userId}", DateTime.UtcNow.AddMinutes(15));
    }

    private sealed class FakeRefreshTokenSecretService : IRefreshTokenSecretService
    {
        private int _counter;

        public string GenerateRawToken() => $"raw-token-{Interlocked.Increment(ref _counter)}";

        public string Hash(string rawToken) => $"hash-of:{rawToken}";
    }
}
