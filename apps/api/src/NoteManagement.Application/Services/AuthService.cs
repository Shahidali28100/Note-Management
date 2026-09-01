using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NoteManagement.Application.DTOs.Auth;
using NoteManagement.Application.Exceptions;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Services;

/// <summary>The one Application-layer auth orchestrator (spec: authentication capability).</summary>
public sealed class AuthService : IAuthService
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OtpReissueCooldown = TimeSpan.FromSeconds(60);

    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IRefreshTokenSecretService _refreshTokenSecretService;
    private readonly IPasswordResetOtpRepository _passwordResetOtpRepository;
    private readonly IOtpGenerator _otpGenerator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenSecretService refreshTokenSecretService,
        IPasswordResetOtpRepository passwordResetOtpRepository,
        IOtpGenerator otpGenerator,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _refreshTokenSecretService = refreshTokenSecretService;
        _passwordResetOtpRepository = passwordResetOtpRepository;
        _otpGenerator = otpGenerator;
        _logger = logger;
    }

    /// <summary>FRS-AUTH-001. Relies on the Users.Email unique index (translated to DuplicateEmailException by UnitOfWork/here) rather than a pre-check, to avoid a check-then-insert race.</summary>
    public async Task<UserDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken)
    {
        var passwordHash = _passwordHasher.Hash(request.Password);
        var user = User.Register(request.Name, request.Email, passwordHash);

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            _userRepository.Add(user);
            try
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (UniqueConstraintViolationException ex)
            {
                throw new DuplicateEmailException(request.Email, ex);
            }
        }, cancellationToken);

        return new UserDto(user.Id, user.Name, user.Email);
    }

    /// <summary>FRS-AUTH-002. "Unknown email" and "wrong password" throw the same exception — never reveals which.</summary>
    public async Task<AuthTokensDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        return await IssueTokensAsync(user.Id, cancellationToken);
    }

    /// <summary>FRS-AUTH-003 + reuse-detection. Rotates on every use; presenting an already-revoked token cascades to revoking every other active session for that user.</summary>
    public async Task<AuthTokensDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken)
    {
        var tokenHash = _refreshTokenSecretService.Hash(request.RefreshToken);
        var existingToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null)
        {
            throw new InvalidRefreshTokenException();
        }

        if (existingToken.RevokedAt is not null)
        {
            // Reuse of an already-revoked token (by a prior rotation, or by logout) is theft
            // evidence — cascade-revoke every other active session for this user.
            await _unitOfWork.RunInTransactionAsync(
                ct => _refreshTokenRepository.RevokeAllActiveForUserAsync(existingToken.UserId, ct),
                cancellationToken);
            throw new InvalidRefreshTokenException();
        }

        if (!existingToken.IsActive)
        {
            // Naturally expired, never used — no reuse signal, no cascade.
            throw new InvalidRefreshTokenException();
        }

        var userId = existingToken.UserId;
        var (accessToken, accessTokenExpiresAtUtc) = _jwtTokenGenerator.GenerateAccessToken(userId);
        var rawRefreshToken = _refreshTokenSecretService.GenerateRawToken();
        var newRefreshToken = RefreshToken.Issue(userId, _refreshTokenSecretService.Hash(rawRefreshToken), DateTime.UtcNow.Add(RefreshTokenLifetime));

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            existingToken.Revoke();
            _refreshTokenRepository.Add(newRefreshToken);
            // existingToken and newRefreshToken are tracked on the same DbContext — one
            // SaveChangesAsync call persists the revoke and the insert atomically together.
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return new AuthTokensDto(accessToken, rawRefreshToken, accessTokenExpiresAtUtc);
    }

    /// <summary>FRS-AUTH-004. Possession of the still-valid refresh token is itself the credential; revokes only that one session.</summary>
    public async Task LogoutAsync(LogoutRequestDto request, CancellationToken cancellationToken)
    {
        var tokenHash = _refreshTokenSecretService.Hash(request.RefreshToken);
        var existingToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null || !existingToken.IsActive)
        {
            throw new InvalidRefreshTokenException();
        }

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            existingToken.Revoke();
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    /// <summary>Backs GET /api/auth/me — looks the user up rather than trusting claims blindly.</summary>
    public async Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            // Not reachable in practice — a valid access token's sub always maps to a real user
            // (no user-delete flow exists in this ticket) — fail loudly rather than silently.
            throw new InvalidOperationException($"User '{userId}' referenced by a valid access token no longer exists.");
        }

        return new UserDto(user.Id, user.Name, user.Email);
    }

    /// <summary>
    /// FRS-AUTH-005. The response is identical regardless of whether the email exists or the
    /// request lands inside the cooldown window — every early return below still ends in the same
    /// generic 200 from the controller, so nothing here signals account existence.
    /// </summary>
    public async Task ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var latest = await _passwordResetOtpRepository.GetLatestForUserAsync(user.Id, cancellationToken);
        if (latest is not null && latest.CreatedAt > now - OtpReissueCooldown)
        {
            return; // Within cooldown — do not reissue; the existing OTP keeps its original expiry.
        }

        var rawOtp = _otpGenerator.GenerateRawOtp();
        var otp = PasswordResetOtp.Issue(user.Id, _otpGenerator.Hash(rawOtp), now.Add(OtpLifetime), now);

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            // Only the newest OTP is ever valid — supersede whatever was outstanding before adding this one.
            await _passwordResetOtpRepository.InvalidateAllActiveForUserAsync(user.Id, ct);
            _passwordResetOtpRepository.Add(otp);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        // AGENTS.md §11 / SDS §62 explicitly allow logging the OTP — no real email provider exists.
        _logger.LogInformation("Password reset OTP for user {UserId}: {Otp}", user.Id, rawOtp);
    }

    /// <summary>
    /// FRS-AUTH-006. Every rejection path throws the same InvalidPasswordResetException — unknown
    /// email, no active OTP, and a hash mismatch are all indistinguishable to the caller.
    /// </summary>
    public async Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        var activeOtp = user is null
            ? null
            : await _passwordResetOtpRepository.GetActiveForUserAsync(user.Id, cancellationToken);

        if (user is null || activeOtp is null)
        {
            throw new InvalidPasswordResetException();
        }

        // Constant-time comparison — a plain string/== check short-circuits on the first
        // differing byte, leaking timing information about how many leading hex characters of
        // the guess were correct. Both hashes are always the same fixed length (SHA-256 hex is
        // 64 chars), so length itself carries no signal either.
        var submittedHash = _otpGenerator.Hash(request.Otp);
        var hashesMatch = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(submittedHash),
            Encoding.UTF8.GetBytes(activeOtp.OtpHash));

        if (!hashesMatch)
        {
            activeOtp.RegisterFailedAttempt();
            await _unitOfWork.RunInTransactionAsync(ct => _unitOfWork.SaveChangesAsync(ct), cancellationToken);
            throw new InvalidPasswordResetException();
        }

        var newPasswordHash = _passwordHasher.Hash(request.NewPassword);

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            user.ChangePassword(newPasswordHash);
            // Marks activeOtp (and any stray other outstanding OTP) used, in one atomic statement.
            await _passwordResetOtpRepository.InvalidateAllActiveForUserAsync(user.Id, ct);
            await _refreshTokenRepository.RevokeAllActiveForUserAsync(user.Id, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    private async Task<AuthTokensDto> IssueTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (accessToken, accessTokenExpiresAtUtc) = _jwtTokenGenerator.GenerateAccessToken(userId);
        var rawRefreshToken = _refreshTokenSecretService.GenerateRawToken();
        var refreshToken = RefreshToken.Issue(userId, _refreshTokenSecretService.Hash(rawRefreshToken), DateTime.UtcNow.Add(RefreshTokenLifetime));

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            _refreshTokenRepository.Add(refreshToken);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return new AuthTokensDto(accessToken, rawRefreshToken, accessTokenExpiresAtUtc);
    }
}
