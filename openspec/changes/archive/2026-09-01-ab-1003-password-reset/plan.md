
# Plan: ab-1003-password-reset

Source: `proposal.md`, `specs/authentication/spec.md`, `delta-openapi.yaml`, `docs/SDS.md`, `AGENTS.md`, `apps/api/CLAUDE.md`. Extends AB-1002's `authentication` capability in place — same `AuthController`, same `AuthService`, same layering — rather than introducing a parallel service/controller for password reset.

## 0. Reused facts from AB-1001/AB-1002 (already resolved, not re-litigated)

- `.NET SDK`/LocalDB/`dotnet-ef` environment, Central Package Management (`apps/api/Directory.Packages.props`), `packages/shared`'s no-build-step TS — all unchanged from AB-1002.
- Layering/DI/testing template: `AddApplication()`/`AddInfrastructure(configuration)`, hand-rolled test fakes (no Moq/NSubstitute), fail-fast `?? throw` config reads — reused as-is.
- `IUserRepository`, `IRefreshTokenRepository` (incl. its existing `RevokeAllActiveForUserAsync`), `IUnitOfWork`, `IPasswordHasher`, `PasswordPolicyAttribute`, `ProblemDetailsExceptionHandler`, `AuthController`, `AuthService` all already exist and are extended in place, not duplicated.

## 1. New package versions (live-verified via nuget.org)

| Package | Version | Where | Role |
|---|---|---|---|
| Microsoft.Extensions.Logging.Abstractions | `8.0.2` | `NoteManagement.Application` | `ILogger<AuthService>` — needed so `ForgotPasswordAsync` can log the raw OTP (SDS §62/AGENTS.md §11 explicitly allow this). Matches the 8.0.2 patch already used for `Microsoft.Extensions.DependencyInjection.Abstractions` in the same project. Not previously referenced by Application (confirmed: only `DependencyInjection.Abstractions` is there today) — ASP.NET Core's own `ILogger<T>` DI registration in `Program.cs` satisfies it at runtime, no new registration code needed. |

No other new package is needed: OTP generation reuses `System.Security.Cryptography.RandomNumberGenerator` (BCL, already used by `RefreshTokenSecretService`) and `SHA256`, both already available to `NoteManagement.Infrastructure`.

## 2. File tree to create / modify

```
apps/api/
├── Directory.Packages.props                                       # MODIFIED — add Logging.Abstractions row from §1
├── src/
│   ├── NoteManagement.Domain/
│   │   └── Entities/
│   │       ├── PasswordResetOtp.cs                                 (NEW)
│   │       └── User.cs                                              # MODIFIED — add ChangePassword(newPasswordHash)
│   │
│   ├── NoteManagement.Application/
│   │   ├── NoteManagement.Application.csproj                       # MODIFIED — add Logging.Abstractions package ref
│   │   ├── DTOs/Auth/
│   │   │   ├── ForgotPasswordRequestDto.cs                         (NEW)
│   │   │   ├── ResetPasswordRequestDto.cs                          (NEW)
│   │   │   └── MessageResponseDto.cs                                (NEW)
│   │   ├── Exceptions/
│   │   │   └── InvalidPasswordResetException.cs                    (NEW)
│   │   ├── Interfaces/
│   │   │   ├── IPasswordResetOtpRepository.cs                      (NEW)
│   │   │   ├── IOtpGenerator.cs                                     (NEW)
│   │   │   └── IAuthService.cs                                       # MODIFIED — +2 methods
│   │   └── Services/
│   │       └── AuthService.cs                                        # MODIFIED — +2 methods, ctor gains 3 deps
│   │
│   ├── NoteManagement.Infrastructure/
│   │   ├── Configurations/
│   │   │   └── PasswordResetOtpConfiguration.cs                     (NEW)
│   │   ├── Data/
│   │   │   └── ApplicationDbContext.cs                               # MODIFIED — +DbSet<PasswordResetOtp>
│   │   ├── Repositories/
│   │   │   └── PasswordResetOtpRepository.cs                        (NEW)
│   │   ├── Authentication/
│   │   │   └── OtpGenerator.cs                                       (NEW)
│   │   ├── Migrations/
│   │   │   └── <timestamp>_AddPasswordResetOtps.cs                  (NEW)
│   │   └── DependencyInjection.cs                                     # MODIFIED — register the 2 new interfaces
│   │
│   └── NoteManagement.Api/
│       ├── Controllers/
│       │   └── AuthController.cs                                     # MODIFIED — +ForgotPassword/+ResetPassword actions
│       └── Middleware/
│           └── ProblemDetailsExceptionHandler.cs                     # MODIFIED — map InvalidPasswordResetException → 400
│
└── tests/
    ├── NoteManagement.Tests.Unit/
    │   ├── Domain/
    │   │   └── PasswordResetOtpTests.cs                              (NEW)
    │   └── Application/
    │       └── AuthServiceTests.cs                                    # MODIFIED — +ForgotPasswordAsync_*/ResetPasswordAsync_* + 2 new fakes
    └── NoteManagement.Tests.Integration/
        ├── Infrastructure/
        │   └── OtpGeneratorTests.cs                                   (NEW)
        └── Api/
            └── AuthControllerTests.cs                                 # MODIFIED — +ForgotPassword_*/ResetPassword_* + a second factory (§9)

packages/shared/
└── src/
    ├── types/auth.ts                                                  # MODIFIED — export 3 new types
    ├── schemas/auth.ts                                                 # MODIFIED — add 3 new Zod schemas
    └── index.ts                                                        # MODIFIED — export the 3 new schemas
```

No changes to `Program.cs` or `appsettings*.json` — both new endpoints are `[AllowAnonymous]` on the existing `AuthController`/JWT pipeline, and OTP lifetime/cooldown/attempt-limit are code constants (same treatment as `AuthService`'s existing `RefreshTokenLifetime` constant), not configuration.

## 3. Domain layer

```csharp
// Domain/Entities/PasswordResetOtp.cs
namespace NoteManagement.Domain.Entities;

/// <summary>
/// A password-reset OTP (AB-1003 / FRS-AUTH-005/006, SDS §12 + AttemptCount — see proposal.md
/// Impact). Only the hash is ever persisted; the raw 6-digit code is handed to the caller
/// exactly once (to log), never stored or returned via the API.
///
/// UsedAt is reused as the single "no longer usable" flag for three distinct business events —
/// a successful reset, being superseded by a newer OTP, and being locked out after too many
/// incorrect attempts — because all three share the same externally observable behavior (the
/// code stops working), and SDS §12's baseline schema has no room for a separate "why" column.
/// </summary>
public sealed class PasswordResetOtp
{
    public const int MaxAttempts = 5;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string OtpHash { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>EF Core materialization only — invariants are enforced via <see cref="Issue"/>.</summary>
    private PasswordResetOtp()
    {
    }

    public static PasswordResetOtp Issue(Guid userId, string otpHash, DateTime expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        OtpHash = otpHash,
        ExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow,
    };

    /// <summary>
    /// Usable only if not yet consumed/invalidated and not yet expired. Attempt-count lockout
    /// does not need its own check here — RegisterFailedAttempt reaching MaxAttempts calls
    /// Invalidate() immediately, so UsedAt already reflects the lockout.
    /// </summary>
    public bool IsActive => UsedAt is null && ExpiresAt > DateTime.UtcNow;

    /// <summary>Idempotent — mirrors RefreshToken.Revoke(). Used for all three "no longer usable" events described above.</summary>
    public void Invalidate() => UsedAt ??= DateTime.UtcNow;

    /// <summary>Increments the incorrect-attempt counter; on the 5th, locks (invalidates) this OTP even though it hasn't expired.</summary>
    public void RegisterFailedAttempt()
    {
        AttemptCount++;
        if (AttemptCount >= MaxAttempts)
        {
            Invalidate();
        }
    }
}
```

```csharp
// Domain/Entities/User.cs — MODIFIED, one method added
public void ChangePassword(string newPasswordHash)
{
    PasswordHash = newPasswordHash;
    UpdatedAt = DateTime.UtcNow;
}
```

## 4. Application layer

**DTOs** (match `delta-openapi.yaml` field-for-field; attributes target constructor parameters, per `RegisterRequestDto`'s established remark):

```csharp
public sealed record ForgotPasswordRequestDto(
    [Required, EmailAddress, StringLength(320)] string Email);

public sealed record ResetPasswordRequestDto(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Code must be 6 digits.")] string Otp,
    [Required, MinLength(8), PasswordPolicy] string NewPassword);

public sealed record MessageResponseDto(string Message);
```

`MessageResponseDto` is shared by both endpoints' `200` responses — the message text itself is chosen by the controller (see §7), not the DTO.

**Exception:**

```csharp
namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Thrown by AuthService.ResetPasswordAsync for every rejection reason (unknown email, wrong
/// OTP, expired OTP, already-used/locked-out OTP) — deliberately generic, mirrors
/// InvalidCredentialsException's "never reveal which part was wrong" precedent. Mapped to 400
/// by ProblemDetailsExceptionHandler (not 401 — this isn't bearer-token authentication, it's
/// validating a one-time code against a submitted email).
/// </summary>
public sealed class InvalidPasswordResetException : Exception
{
    public InvalidPasswordResetException()
        : base("The reset code is invalid or has expired.")
    {
    }
}
```

**Interfaces** (implemented in Infrastructure, same inversion shape as `IRefreshTokenRepository`/`IRefreshTokenSecretService`):

```csharp
public interface IPasswordResetOtpRepository
{
    void Add(PasswordResetOtp otp);

    /// <summary>Most recently created OTP for this user, regardless of used/expired state — backs the 60s reissue cooldown, which must key off "last issued", not "last active".</summary>
    Task<PasswordResetOtp?> GetLatestForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The currently valid (unused, unexpired) OTP for this user, if any — at most one should ever exist, by construction. Backs reset-password validation and attempt counting.</summary>
    Task<PasswordResetOtp?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomic bulk UPDATE — marks every currently-unused OTP row for this user as used, in one
    /// statement (SDS §47's no-read-modify-write principle, same shape as
    /// IRefreshTokenRepository.RevokeAllActiveForUserAsync). Reused for two different business
    /// moments: superseding a prior OTP when a new one is issued, and consuming-plus-invalidating
    /// everything-else on a successful reset.
    /// </summary>
    Task InvalidateAllActiveForUserAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IOtpGenerator
{
    /// <summary>Cryptographically random 6-digit numeric code, zero-padded (e.g. "048392").</summary>
    string GenerateRawOtp();

    /// <summary>SHA-256 hex — same shape and purpose as IRefreshTokenSecretService.Hash.</summary>
    string Hash(string rawOtp);
}
```

**`IAuthService`** gains:

```csharp
Task ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken);

Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken);
```

**`AuthService`** gains 3 constructor dependencies (`IPasswordResetOtpRepository`, `IOtpGenerator`, `ILogger<AuthService>`) and 2 methods:

```csharp
private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);
private static readonly TimeSpan OtpReissueCooldown = TimeSpan.FromSeconds(60);

/// <summary>
/// FRS-AUTH-005. The response is identical regardless of whether the email exists or the
/// request lands inside the cooldown window — every early return below still ends in the same
/// generic 200 from the controller (§7), so nothing here signals account existence.
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
    var otp = PasswordResetOtp.Issue(user.Id, _otpGenerator.Hash(rawOtp), now.Add(OtpLifetime));

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
```

The hash comparison uses `CryptographicOperations.FixedTimeEquals` (`System.Security.Cryptography`) rather than `string`/`==` equality — a plain comparison short-circuits on the first differing byte, leaking timing information about how many leading hex characters of the guess were correct. It takes two `ReadOnlySpan<byte>`, so both hex strings are encoded via `Encoding.UTF8.GetBytes` (`System.Text`) first; both are always the same fixed length (SHA-256 hex is 64 chars), so the length itself carries no signal either. This is the same "no hand-rolled crypto, no shortcuts" standard already applied to `RandomNumberGenerator.GetInt32` (unbiased OTP generation) and `PasswordHasher`/`RefreshTokenSecretService` (BCL primitives only) — `AuthService.cs` gains `using System.Security.Cryptography;` and `using System.Text;` for it. (`IRefreshTokenRepository.GetByTokenHashAsync`'s existing SQL-level `==` lookup is unaffected by this change — a database index lookup isn't a comparable in-process timing side-channel, and reworking it is out of scope here.)

`ResetPasswordAsync` reuses `IRefreshTokenRepository.RevokeAllActiveForUserAsync` — the exact method AB-1002 built for refresh-token reuse-detection — for the "revoke all sessions on password change" side effect. No new refresh-token repository method needed.

The wrong-OTP path mutates the already-loaded `activeOtp` (change-tracked entity) and calls `SaveChangesAsync`, the same pattern `RefreshAsync` already uses for `existingToken.Revoke()` — a single already-loaded row's field, not a hot concurrent counter like `ShareLinks.ViewCount`, so change-tracking is the right tool here (bulk `ExecuteUpdateAsync` is reserved for the multi-row `InvalidateAllActiveForUserAsync`/`RevokeAllActiveForUserAsync` operations, matching existing convention exactly).

**Known, accepted risk**: two concurrent incorrect-OTP submissions for the same user could both read `AttemptCount` before either write lands, under-counting by one attempt. This mirrors the precision AB-1002 already accepts elsewhere (e.g. it is not a security-critical atomic counter like `ViewCount`); a single-user attacker racing their own guesses gains at most one extra attempt, bounded by the 10-minute OTP expiry either way. Not fixed in this ticket.

## 5. Infrastructure layer

**EF Core configuration:**

```csharp
public sealed class PasswordResetOtpConfiguration : IEntityTypeConfiguration<PasswordResetOtp>
{
    public void Configure(EntityTypeBuilder<PasswordResetOtp> builder)
    {
        builder.ToTable("PasswordResetOtps");

        builder.HasKey(o => o.Id);

        // SHA-256 hex is 64 chars — headroom kept, same as RefreshTokenConfiguration.TokenHash.
        // Deliberately NOT unique (unlike RefreshTokens.TokenHash): a 6-digit OTP has only
        // 1,000,000 possible values, so hash collisions across different users are expected
        // over time and are not a uniqueness violation.
        builder.Property(o => o.OtpHash)
            .HasMaxLength(128)
            .IsRequired();

        // Supports GetActiveForUserAsync's WHERE UserId = X AND UsedAt IS NULL AND ExpiresAt > now,
        // and the InvalidateAllActiveForUserAsync bulk update — same shape as
        // RefreshTokenConfiguration's (UserId, RevokedAt) index.
        builder.HasIndex(o => new { o.UserId, o.UsedAt });

        builder.Property(o => o.ExpiresAt)
            .IsRequired();

        builder.Property(o => o.AttemptCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        // UsedAt is nullable by default (DateTime?) — no IsRequired() call needed.

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**`ApplicationDbContext`** gains:

```csharp
public DbSet<PasswordResetOtp> PasswordResetOtps => Set<PasswordResetOtp>();
```

(`OnModelCreating`'s `ApplyConfigurationsFromAssembly` call already picks up the new configuration automatically — no change needed there.)

**`PasswordResetOtpRepository`:**

```csharp
public sealed class PasswordResetOtpRepository : IPasswordResetOtpRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PasswordResetOtpRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(PasswordResetOtp otp) => _dbContext.PasswordResetOtps.Add(otp);

    public Task<PasswordResetOtp?> GetLatestForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.PasswordResetOtps
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PasswordResetOtp?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _dbContext.PasswordResetOtps
            .Where(o => o.UserId == userId && o.UsedAt == null && o.ExpiresAt > now)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Single atomic UPDATE — no read-modify-write, same shape as RefreshTokenRepository.RevokeAllActiveForUserAsync.</summary>
    public Task InvalidateAllActiveForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _dbContext.PasswordResetOtps
            .Where(o => o.UserId == userId && o.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.UsedAt, now), cancellationToken);
    }
}
```

**`OtpGenerator`:**

```csharp
public sealed class OtpGenerator : IOtpGenerator
{
    private const int OtpExclusiveUpperBound = 1_000_000; // 6 digits: 000000–999999

    public string GenerateRawOtp() =>
        RandomNumberGenerator.GetInt32(OtpExclusiveUpperBound).ToString("D6");

    // Same SHA-256-hex shape as RefreshTokenSecretService.Hash — Convert.ToHexStringLower isn't
    // available until .NET 9, this project targets net8.0.
    public string Hash(string rawOtp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawOtp))).ToLowerInvariant();
}
```

`RandomNumberGenerator.GetInt32(upperBoundExclusive)` is already unbiased (no naive `% 1_000_000` modulo-bias mistake) — matches the "no hand-rolled crypto" principle `RefreshTokenSecretService`/`PasswordHasher` already follow.

**`DependencyInjection.cs`** (Infrastructure) additions:

```csharp
services.AddScoped<IPasswordResetOtpRepository, PasswordResetOtpRepository>();
services.AddSingleton<IOtpGenerator, OtpGenerator>(); // stateless, same treatment as IRefreshTokenSecretService
```

`ILogger<AuthService>` needs no registration here — ASP.NET Core's default host already registers the open-generic `ILogger<T>` once logging is configured, which `Program.cs` already does.

## 6. EF Core migration

- **Name**: `AddPasswordResetOtps`
- **Command**:
  ```bash
  dotnet ef migrations add AddPasswordResetOtps \
    --project apps/api/src/NoteManagement.Infrastructure \
    --startup-project apps/api/src/NoteManagement.Api

  dotnet ef database update \
    --project apps/api/src/NoteManagement.Infrastructure \
    --startup-project apps/api/src/NoteManagement.Api
  ```
- **Entity changes**: adds the `PasswordResetOtps` table, its `(UserId, UsedAt)` index, and the `PasswordResetOtps.UserId → Users.Id` FK (cascade delete).
- **Backward compatible**: yes — purely additive on top of AB-1002's `AddUsersAndRefreshTokens` migration; no existing table/column is touched.

## 7. Api layer

**`AuthController`** gains two `[AllowAnonymous]` actions, following the existing four's shape exactly:

```csharp
/// <summary>FRS-AUTH-005. Always 200 — see AuthService.ForgotPasswordAsync's remarks on why.</summary>
[HttpPost("forgot-password")]
[AllowAnonymous]
[ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<MessageResponseDto>> ForgotPassword(ForgotPasswordRequestDto request, CancellationToken cancellationToken)
{
    await _authService.ForgotPasswordAsync(request, cancellationToken);
    return Ok(new MessageResponseDto("If that email is registered, a password reset code has been sent."));
}

/// <summary>FRS-AUTH-006.</summary>
[HttpPost("reset-password")]
[AllowAnonymous]
[ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<MessageResponseDto>> ResetPassword(ResetPasswordRequestDto request, CancellationToken cancellationToken)
{
    await _authService.ResetPasswordAsync(request, cancellationToken);
    return Ok(new MessageResponseDto("Password has been reset successfully."));
}
```

No try/catch — `ProblemDetailsExceptionHandler` gains one mapping:

```csharp
InvalidPasswordResetException => (StatusCodes.Status400BadRequest, "Invalid password reset request"),
```

(inserted into the existing `switch` expression alongside `DuplicateEmailException`/`InvalidCredentialsException`/`InvalidRefreshTokenException`; anything unmapped still falls through to the generic 500, unchanged.)

## 8. Shared TS contracts (`packages/shared`)

`src/schemas/auth.ts` gains:

```typescript
export const forgotPasswordRequestSchema = z.object({
  email: z.string().email().max(320),
});
export type ForgotPasswordRequest = z.infer<typeof forgotPasswordRequestSchema>;

export const resetPasswordRequestSchema = z.object({
  email: z.string().email().max(320),
  otp: z.string().regex(/^\d{6}$/, 'Code must be 6 digits.'),
  newPassword: z
    .string()
    .min(8)
    .regex(/[A-Za-z]/, 'Password must contain at least one letter.')
    .regex(/[0-9]/, 'Password must contain at least one digit.'),
});
export type ResetPasswordRequest = z.infer<typeof resetPasswordRequestSchema>;

export const messageResponseSchema = z.object({ message: z.string() });
export type MessageResponse = z.infer<typeof messageResponseSchema>;
```

`src/types/auth.ts` re-exports the 3 new types the same way it already re-exports the AB-1002 ones (`export type { ForgotPasswordRequest, ResetPasswordRequest, MessageResponse } from '../schemas/auth';`). `src/index.ts` adds the 3 new schema values to its existing named export list from `./schemas/auth`. No new dependency — `zod` is already a `packages/shared` dependency.

## 9. Reuse of existing code / patterns

- **Layering + DI-per-layer registration**, **fail-fast config style**, **`AddProblemDetails()`/`UseExceptionHandler()`**, **`[ApiController]` automatic model validation** — all reused as-is, only extended (same list AB-1002's plan.md already established).
- **`IRefreshTokenRepository.RevokeAllActiveForUserAsync`** — reused verbatim for the "revoke all sessions" side effect of a successful reset; no new refresh-token code.
- **`PasswordPolicyAttribute`** — reused verbatim on `ResetPasswordRequestDto.NewPassword` (per the user's clarifying answer: reset uses the same policy as registration).
- **Hand-rolled test fakes** (`AuthServiceTests`'s existing convention: private nested fake classes + a `CreateSut` factory) — extended with `FakePasswordResetOtpRepository` and `FakeOtpGenerator` (an incrementing-counter fake, same shape as `FakeRefreshTokenSecretService`), plus a `NullLogger<AuthService>.Instance` passed for the new `ILogger<AuthService>` parameter (no fake class needed — `Microsoft.Extensions.Logging.Abstractions.NullLogger<T>` is a built-in no-op).
- **Integration testing a value that's only ever logged, never returned via HTTP** (new problem this ticket introduces): `AuthControllerTests`'s existing `_factory`/`_client` (real `OtpGenerator`, real console logging) is sufficient for tests that don't need to know the raw code (unknown-vs-registered-email response parity, cooldown suppression). Tests that must complete a full reset (success, wrong-OTP, lockout) need to know the code in advance, so they use a **second** `WebApplicationFactory<Program>`, configured the same way as `_factory` (`UseSetting("ConnectionStrings:DefaultConnection", ...)`, its own isolated LocalDB database) plus one extra `ConfigureTestServices` call replacing `IOtpGenerator` with a test double that returns a pre-set, ordered sequence of codes (e.g. `new SequentialOtpGenerator("111111", "222222")`, throwing if exhausted, hashing exactly like the real `OtpGenerator`) — this is the same "swap one production seam for a deterministic test double" idea AB-1002's `AuthControllerTests.BuildAccessToken` helper already used for constructing an expired/tampered JWT. `SequentialOtpGenerator` is a private nested class in `AuthControllerTests.cs`, matching the file's existing fake-class placement convention.

## 10. Checkpoint commands

Run in order; fix and re-run on first failure before moving to the next (`CLAUDE.md` Quality Gates).

**Backend** (after §3–§7 file changes):
```bash
dotnet ef migrations add AddPasswordResetOtps --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
```

**Frontend/shared** (after §8 file changes — `packages/shared` only, no `apps/web` changes this ticket):
```bash
pnpm install
pnpm lint --max-warnings 0
pnpm build
pnpm test
```

**Full-repo gate before this ticket is considered done:**
```bash
pnpm lint --max-warnings 0
pnpm build
pnpm test --coverage
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
```

## 11. Tests planned (spec scenario → test)

| Spec scenario | Test |
|---|---|
| Registered email issues an OTP | `AuthServiceTests.ForgotPasswordAsync_WithRegisteredEmail_IssuesHashesAndLogsOtp` (Unit) + `AuthControllerTests.ForgotPassword_WithRegisteredEmail_Returns200` (Integration) |
| Unknown email gives the same generic response | `AuthServiceTests.ForgotPasswordAsync_WithUnknownEmail_DoesNothing` (Unit) + `AuthControllerTests.ForgotPassword_WithUnknownAndRegisteredEmail_ReturnsIdenticalResponse` (Integration — asserts identical status + body for both) |
| New OTP invalidates the previous one | `AuthServiceTests.ForgotPasswordAsync_CalledTwiceOutsideCooldown_InvalidatesPreviousOtp` (Unit, fake repo asserts old OTP's `UsedAt` is set) + `AuthControllerTests.ResetPassword_WithSupersededOtp_Returns400` (Integration, via the `SequentialOtpGenerator` factory from §9) |
| Repeat request within cooldown does not reissue an OTP | `AuthServiceTests.ForgotPasswordAsync_WithinCooldown_DoesNotIssueNewOtp` (Unit) + `AuthControllerTests.ForgotPassword_CalledTwiceQuickly_ReturnsSame200BothTimes` (Integration) |
| Successful password reset | `AuthServiceTests.ResetPasswordAsync_WithValidOtp_UpdatesPasswordAndMarksOtpUsed` (Unit) + `AuthControllerTests.ResetPassword_WithValidOtp_Returns200` (Integration, via `SequentialOtpGenerator`) |
| Incorrect OTP rejected | `AuthServiceTests.ResetPasswordAsync_WithWrongOtp_ThrowsAndIncrementsAttemptCount` (Unit) + `AuthControllerTests.ResetPassword_WithWrongOtp_Returns400` (Integration) |
| Expired OTP rejected | `AuthServiceTests.ResetPasswordAsync_WithExpiredOtp_Throws` (Unit) |
| Already-used OTP rejected | `AuthServiceTests.ResetPasswordAsync_WithAlreadyUsedOtp_Throws` (Unit) + `AuthControllerTests.ResetPassword_CalledTwiceWithSameOtp_SecondCallReturns400` (Integration) |
| OTP locked out after 5 incorrect attempts | `AuthServiceTests.ResetPasswordAsync_After5WrongAttempts_LocksOtpEvenWithCorrectCode` (Unit) + `AuthControllerTests.ResetPassword_After5WrongAttempts_CorrectCodeSubsequentlyRejected` (Integration) |
| Unknown email rejected with the same generic error | `AuthServiceTests.ResetPasswordAsync_WithUnknownEmail_ThrowsSameExceptionAsWrongOtp` (Unit) |
| Password below policy rejected | `AuthControllerTests.ResetPassword_WithWeakNewPassword_Returns400` (Integration — DataAnnotations short-circuits before `AuthService`, same precedent as `Register_WithWeakPassword_Returns400`) |
| Successful reset revokes all sessions | `AuthServiceTests.ResetPasswordAsync_WithActiveRefreshTokens_RevokesAllOfThem` (Unit, fake `IRefreshTokenRepository` asserts `RevokeAllActiveForUserCalls`) + `AuthControllerTests.ResetPassword_ThenRefreshWithOldToken_Returns401` (Integration, end-to-end proof) |
| Successful reset invalidates other outstanding OTPs | Covered by the "New OTP invalidates the previous one" + "Successful password reset" rows above (both exercise `InvalidateAllActiveForUserAsync`) — no separate test |
| `PasswordResetOtp.IsActive`/`Invalidate()`/`RegisterFailedAttempt()` domain rules | `PasswordResetOtpTests` (Unit, Domain — no infrastructure, matches `RefreshTokenTests`' precedent): `IsActive_WhenNotUsedAndNotExpired_ReturnsTrue`, `IsActive_WhenUsed_ReturnsFalse`, `IsActive_WhenExpired_ReturnsFalse`, `RegisterFailedAttempt_BelowMaxAttempts_DoesNotInvalidate`, `RegisterFailedAttempt_ReachingMaxAttempts_Invalidates`, `Invalidate_WhenCalledTwice_KeepsFirstTimestamp` |
| `OtpGenerator` primitives | `OtpGeneratorTests` (Integration/Infrastructure, matches `RefreshTokenSecretServiceTests`' precedent): `GenerateRawOtp_ProducesSixDigitCodes`, `Hash_IsDeterministic_AndDiffersFromRawOtp` |

Every named test follows `Method_Condition_ExpectedResult` (AGENTS.md §6/§10).

## 12. Explicitly not doing in this ticket

- Any frontend auth UI consuming these endpoints (AB-1010) — `packages/shared` additions are the only cross-boundary artifact this ticket produces for that later ticket.
- Rate limiting beyond the per-email 60-second reissue cooldown described above (e.g. IP-based throttling) — not required by FRS/SDS or the approved proposal for this ticket.
- Any change to register/login/refresh/logout behavior (AB-1002, already shipped and unmodified here) beyond the new `IAuthService` methods.
- Any change to `Notes`/`Tags`/etc. — out of scope, AB-1004+.
