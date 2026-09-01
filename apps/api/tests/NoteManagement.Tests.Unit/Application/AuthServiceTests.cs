using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    [TestMethod]
    public async Task ForgotPasswordAsync_WithRegisteredEmail_IssuesHashesAndLogsOtp()
    {
        var user = User.Register("Alice", "alice@example.com", "hashed");
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository();
        var otpGenerator = new FakeOtpGenerator();
        var logger = new FakeLogger();
        var sut = CreateSut(
            userRepository: userRepository,
            passwordResetOtpRepository: passwordResetOtpRepository,
            otpGenerator: otpGenerator,
            logger: logger);
        var request = new ForgotPasswordRequestDto("alice@example.com");

        await sut.ForgotPasswordAsync(request, CancellationToken.None);

        Assert.AreEqual(1, passwordResetOtpRepository.Added.Count);
        Assert.AreEqual(user.Id, passwordResetOtpRepository.Added[0].UserId);
        // The persisted value is a hash, never the raw OTP the caller (console log) receives.
        Assert.AreNotEqual(otpGenerator.LastRawOtp, passwordResetOtpRepository.Added[0].OtpHash);
        Assert.AreEqual(1, logger.Messages.Count);
        StringAssert.Contains(logger.Messages[0], otpGenerator.LastRawOtp!);
    }

    [TestMethod]
    public async Task ForgotPasswordAsync_WithUnknownEmail_DoesNothing()
    {
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository();
        var logger = new FakeLogger();
        var sut = CreateSut(passwordResetOtpRepository: passwordResetOtpRepository, logger: logger);
        var request = new ForgotPasswordRequestDto("nobody@example.com");

        await sut.ForgotPasswordAsync(request, CancellationToken.None);

        Assert.AreEqual(0, passwordResetOtpRepository.Added.Count);
        Assert.AreEqual(0, logger.Messages.Count);
    }

    [TestMethod]
    public async Task ForgotPasswordAsync_CalledTwiceOutsideCooldown_InvalidatesPreviousOtp()
    {
        var user = User.Register("Alice", "alice@example.com", "hashed");
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var previousOtp = PasswordResetOtp.Issue(user.Id, "old-hash", DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow.AddMinutes(-5));
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository(previousOtp);
        var sut = CreateSut(userRepository: userRepository, passwordResetOtpRepository: passwordResetOtpRepository);
        var request = new ForgotPasswordRequestDto("alice@example.com");

        await sut.ForgotPasswordAsync(request, CancellationToken.None);

        Assert.IsNotNull(previousOtp.UsedAt);
        Assert.AreEqual(1, passwordResetOtpRepository.Added.Count);
    }

    [TestMethod]
    public async Task ForgotPasswordAsync_WithinCooldown_DoesNotIssueNewOtp()
    {
        var user = User.Register("Alice", "alice@example.com", "hashed");
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var recentOtp = PasswordResetOtp.Issue(user.Id, "recent-hash", DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow.AddSeconds(-5));
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository(recentOtp);
        var sut = CreateSut(userRepository: userRepository, passwordResetOtpRepository: passwordResetOtpRepository);
        var request = new ForgotPasswordRequestDto("alice@example.com");

        await sut.ForgotPasswordAsync(request, CancellationToken.None);

        Assert.AreEqual(0, passwordResetOtpRepository.Added.Count);
        Assert.IsNull(recentOtp.UsedAt);
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WithValidOtp_UpdatesPasswordAndMarksOtpUsed()
    {
        var passwordHasher = new FakePasswordHasher();
        var user = User.Register("Alice", "alice@example.com", passwordHasher.Hash("OldPassw0rd"));
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var otpGenerator = new FakeOtpGenerator();
        var activeOtp = PasswordResetOtp.Issue(user.Id, otpGenerator.Hash("123456"), DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository(activeOtp);
        var sut = CreateSut(
            userRepository: userRepository,
            passwordResetOtpRepository: passwordResetOtpRepository,
            otpGenerator: otpGenerator,
            passwordHasher: passwordHasher);
        var request = new ResetPasswordRequestDto("alice@example.com", "123456", "NewPassw0rd1");

        await sut.ResetPasswordAsync(request, CancellationToken.None);

        Assert.AreEqual(passwordHasher.Hash("NewPassw0rd1"), user.PasswordHash);
        Assert.IsNotNull(activeOtp.UsedAt);
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WithWrongOtp_ThrowsAndIncrementsAttemptCount()
    {
        var user = User.Register("Alice", "alice@example.com", "hashed");
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var otpGenerator = new FakeOtpGenerator();
        var activeOtp = PasswordResetOtp.Issue(user.Id, otpGenerator.Hash("123456"), DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository(activeOtp);
        var sut = CreateSut(userRepository: userRepository, passwordResetOtpRepository: passwordResetOtpRepository, otpGenerator: otpGenerator);
        var request = new ResetPasswordRequestDto("alice@example.com", "000000", "NewPassw0rd1");

        await Assert.ThrowsExactlyAsync<InvalidPasswordResetException>(() => sut.ResetPasswordAsync(request, CancellationToken.None));

        Assert.AreEqual(1, activeOtp.AttemptCount);
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WithExpiredOtp_Throws()
    {
        var user = User.Register("Alice", "alice@example.com", "hashed");
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var otpGenerator = new FakeOtpGenerator();
        var expiredOtp = PasswordResetOtp.Issue(user.Id, otpGenerator.Hash("123456"), DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(-11));
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository(expiredOtp);
        var sut = CreateSut(userRepository: userRepository, passwordResetOtpRepository: passwordResetOtpRepository, otpGenerator: otpGenerator);
        var request = new ResetPasswordRequestDto("alice@example.com", "123456", "NewPassw0rd1");

        await Assert.ThrowsExactlyAsync<InvalidPasswordResetException>(() => sut.ResetPasswordAsync(request, CancellationToken.None));
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WithAlreadyUsedOtp_Throws()
    {
        var user = User.Register("Alice", "alice@example.com", "hashed");
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var otpGenerator = new FakeOtpGenerator();
        var usedOtp = PasswordResetOtp.Issue(user.Id, otpGenerator.Hash("123456"), DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);
        usedOtp.Invalidate();
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository(usedOtp);
        var sut = CreateSut(userRepository: userRepository, passwordResetOtpRepository: passwordResetOtpRepository, otpGenerator: otpGenerator);
        var request = new ResetPasswordRequestDto("alice@example.com", "123456", "NewPassw0rd1");

        await Assert.ThrowsExactlyAsync<InvalidPasswordResetException>(() => sut.ResetPasswordAsync(request, CancellationToken.None));
    }

    [TestMethod]
    public async Task ResetPasswordAsync_After5WrongAttempts_LocksOtpEvenWithCorrectCode()
    {
        var user = User.Register("Alice", "alice@example.com", "hashed");
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var otpGenerator = new FakeOtpGenerator();
        var activeOtp = PasswordResetOtp.Issue(user.Id, otpGenerator.Hash("123456"), DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository(activeOtp);
        var sut = CreateSut(userRepository: userRepository, passwordResetOtpRepository: passwordResetOtpRepository, otpGenerator: otpGenerator);
        var wrongRequest = new ResetPasswordRequestDto("alice@example.com", "000000", "NewPassw0rd1");

        for (var i = 0; i < PasswordResetOtp.MaxAttempts; i++)
        {
            await Assert.ThrowsExactlyAsync<InvalidPasswordResetException>(() => sut.ResetPasswordAsync(wrongRequest, CancellationToken.None));
        }

        var correctRequest = new ResetPasswordRequestDto("alice@example.com", "123456", "NewPassw0rd1");
        await Assert.ThrowsExactlyAsync<InvalidPasswordResetException>(() => sut.ResetPasswordAsync(correctRequest, CancellationToken.None));
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WithUnknownEmail_ThrowsSameExceptionAsWrongOtp()
    {
        var sut = CreateSut();
        var request = new ResetPasswordRequestDto("nobody@example.com", "123456", "NewPassw0rd1");

        await Assert.ThrowsExactlyAsync<InvalidPasswordResetException>(() => sut.ResetPasswordAsync(request, CancellationToken.None));
    }

    [TestMethod]
    public async Task ResetPasswordAsync_WithActiveRefreshTokens_RevokesAllOfThem()
    {
        var passwordHasher = new FakePasswordHasher();
        var user = User.Register("Alice", "alice@example.com", passwordHasher.Hash("OldPassw0rd"));
        var userRepository = new FakeUserRepository(existingByEmail: user);
        var otpGenerator = new FakeOtpGenerator();
        var activeOtp = PasswordResetOtp.Issue(user.Id, otpGenerator.Hash("123456"), DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow);
        var passwordResetOtpRepository = new FakePasswordResetOtpRepository(activeOtp);
        var refreshTokenRepository = new FakeRefreshTokenRepository();
        var sut = CreateSut(
            userRepository: userRepository,
            passwordResetOtpRepository: passwordResetOtpRepository,
            otpGenerator: otpGenerator,
            passwordHasher: passwordHasher,
            refreshTokenRepository: refreshTokenRepository);
        var request = new ResetPasswordRequestDto("alice@example.com", "123456", "NewPassw0rd1");

        await sut.ResetPasswordAsync(request, CancellationToken.None);

        CollectionAssert.Contains(refreshTokenRepository.RevokeAllActiveForUserCalls, user.Id);
    }

    private static AuthService CreateSut(
        FakeUserRepository? userRepository = null,
        FakeRefreshTokenRepository? refreshTokenRepository = null,
        FakeUnitOfWork? unitOfWork = null,
        FakePasswordHasher? passwordHasher = null,
        FakeJwtTokenGenerator? jwtTokenGenerator = null,
        FakeRefreshTokenSecretService? refreshTokenSecretService = null,
        FakePasswordResetOtpRepository? passwordResetOtpRepository = null,
        FakeOtpGenerator? otpGenerator = null,
        ILogger<AuthService>? logger = null) =>
        new(
            userRepository ?? new FakeUserRepository(),
            refreshTokenRepository ?? new FakeRefreshTokenRepository(),
            unitOfWork ?? new FakeUnitOfWork(),
            passwordHasher ?? new FakePasswordHasher(),
            jwtTokenGenerator ?? new FakeJwtTokenGenerator(),
            refreshTokenSecretService ?? new FakeRefreshTokenSecretService(),
            passwordResetOtpRepository ?? new FakePasswordResetOtpRepository(),
            otpGenerator ?? new FakeOtpGenerator(),
            logger ?? NullLogger<AuthService>.Instance);

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

    private sealed class FakePasswordResetOtpRepository : IPasswordResetOtpRepository
    {
        private readonly List<PasswordResetOtp> _all;

        public FakePasswordResetOtpRepository(params PasswordResetOtp[] existing)
        {
            _all = existing.ToList();
        }

        public List<PasswordResetOtp> Added { get; } = new();

        public List<Guid> InvalidateAllActiveForUserCalls { get; } = new();

        public void Add(PasswordResetOtp otp)
        {
            Added.Add(otp);
            _all.Add(otp);
        }

        public Task<PasswordResetOtp?> GetLatestForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_all.Where(o => o.UserId == userId).OrderByDescending(o => o.CreatedAt).FirstOrDefault());

        public Task<PasswordResetOtp?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_all.Where(o => o.UserId == userId && o.IsActive).OrderByDescending(o => o.CreatedAt).FirstOrDefault());

        public Task InvalidateAllActiveForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            InvalidateAllActiveForUserCalls.Add(userId);
            foreach (var otp in _all.Where(o => o.UserId == userId && o.UsedAt is null))
            {
                otp.Invalidate();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeOtpGenerator : IOtpGenerator
    {
        private int _counter;

        public string? LastRawOtp { get; private set; }

        public string GenerateRawOtp()
        {
            LastRawOtp = $"otp-{Interlocked.Increment(ref _counter)}";
            return LastRawOtp;
        }

        public string Hash(string rawOtp) => $"hash-of:{rawOtp}";
    }

    private sealed class FakeLogger : ILogger<AuthService>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
