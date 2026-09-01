# Tasks: ab-1003-password-reset

Source: `proposal.md`, `plan.md`. Each task references the plan section it implements. `[PARALLEL]` tasks have no dependency on each other and may run in separate git worktrees per SDS §87. Extends AB-1002's `AuthController`/`AuthService`/`authentication` capability in place — no new controller/service files.

## Phase 1: Foundation

Package plumbing and pure data shapes first — nothing here has business logic yet, so it can all land before any service/repository implementation exists.

- [x] 1.1 `Directory.Packages.props`: add the 1 new `<PackageVersion>` row (`Microsoft.Extensions.Logging.Abstractions 8.0.2`) — plan §1
- [x] 1.2 Add the matching `<PackageReference>` (no inline version) to `NoteManagement.Application.csproj` — plan §1

**[PARALLEL] — 1.3 Domain vs. 1.4 Application DTOs/validation/exceptions/interfaces vs. 1.5 shared-package types, once 1.1–1.2 are done:**

- [x] 1.3 Domain — plan §3: `Entities/PasswordResetOtp.cs` (private setters + `Issue` static factory, `MaxAttempts` const, `IsActive`, `Invalidate()`, `RegisterFailedAttempt()`); `Entities/User.cs` — add `ChangePassword(newPasswordHash)`
- [x] 1.4 Application layer, data shapes only (no `AuthService` changes yet — plan §4):
  - `DTOs/Auth/ForgotPasswordRequestDto.cs`, `ResetPasswordRequestDto.cs`, `MessageResponseDto.cs`
  - `Exceptions/InvalidPasswordResetException.cs`
  - `Interfaces/IPasswordResetOtpRepository.cs`, `IOtpGenerator.cs`
  - `Interfaces/IAuthService.cs` — add `ForgotPasswordAsync`/`ResetPasswordAsync` signatures
- [x] 1.5 Shared package — plan §8: `src/schemas/auth.ts` (+`forgotPasswordRequestSchema`, `resetPasswordRequestSchema`, `messageResponseSchema`), `src/types/auth.ts` (+3 re-exported types), `src/index.ts` (+3 schema exports)

**Checkpoint (Phase 1):**
```bash
dotnet build apps/api/NoteManagement.sln          # 0 errors — DTOs/interfaces/entities compile standalone
pnpm install && pnpm build                         # shared package (zod schemas) type-checks
```

## Phase 2: Core implementation

**[PARALLEL] — 2.1–2.5 Infrastructure vs. 2.6 Application service, once Phase 1 is done (the service only needs the *interfaces* from 1.4, not the Infrastructure implementations):**

- [x] 2.1 EF Core configuration — plan §5: `Configurations/PasswordResetOtpConfiguration.cs` (`OtpHash` `HasMaxLength(128)`, **not unique** — a 6-digit OTP collides across users by design; composite `(UserId, UsedAt)` index; `AttemptCount` `HasDefaultValue(0)`; FK to `Users` with `OnDelete(Cascade)`)
- [x] 2.2 `ApplicationDbContext` — plan §5: add `DbSet<PasswordResetOtp> PasswordResetOtps`
- [x] 2.3 `Repositories/PasswordResetOtpRepository.cs` — plan §5: `GetLatestForUserAsync` (ordered by `CreatedAt` desc, any state — backs the cooldown), `GetActiveForUserAsync` (unused + unexpired, ordered by `CreatedAt` desc), `InvalidateAllActiveForUserAsync` (atomic bulk `ExecuteUpdateAsync`, no read-modify-write)
- [x] 2.4 `Authentication/OtpGenerator.cs` — plan §5: `GenerateRawOtp()` via `RandomNumberGenerator.GetInt32(1_000_000).ToString("D6")` (unbiased, no modulo), `Hash()` via `SHA256.HashData` → lowercase hex (same shape as `RefreshTokenSecretService.Hash`)
- [x] 2.5 `Infrastructure/DependencyInjection.cs` — plan §5: register `IPasswordResetOtpRepository` (`AddScoped`), `IOtpGenerator` (`AddSingleton`, stateless)

- [x] 2.6 `Application/Services/AuthService.cs` — plan §4: ctor gains `IPasswordResetOtpRepository`, `IOtpGenerator`, `ILogger<AuthService>`; add `using System.Security.Cryptography;` and `using System.Text;`; add `OtpLifetime`/`OtpReissueCooldown` constants; implement:
  - `ForgotPasswordAsync` — unknown email or within-cooldown both return with no observable difference; otherwise supersede any prior OTP (`InvalidateAllActiveForUserAsync`), issue + persist the new one inside a transaction, then `_logger.LogInformation` the raw code
  - `ResetPasswordAsync` — unknown email / no active OTP → `InvalidatePasswordResetException`; hash comparison via **`CryptographicOperations.FixedTimeEquals`** on the UTF-8 bytes of both hex hashes (constant-time — not `string`/`==`, which would leak timing information about how many leading hex characters matched); wrong hash → `RegisterFailedAttempt()` + save + throw; success → `user.ChangePassword`, `InvalidateAllActiveForUserAsync`, `_refreshTokenRepository.RevokeAllActiveForUserAsync` — all inside one transaction

**Checkpoint (Phase 2):**
```bash
dotnet build apps/api/NoteManagement.sln          # 0 errors — Infrastructure + Application compile (Api not wired yet)
```

## Phase 3: Integration

Wires controller → service → repository → DbContext → SQL Server, the exception mapping, and the migration that makes the table exist.

- [x] 3.1 `Api/Controllers/AuthController.cs` — plan §7: `ForgotPassword` (`[AllowAnonymous]`, 200, fixed generic message), `ResetPassword` (`[AllowAnonymous]`, 200, fixed success message)
- [x] 3.2 `Api/Middleware/ProblemDetailsExceptionHandler.cs` — plan §7: add `InvalidPasswordResetException` → 400 to the existing `switch` expression
- [x] 3.3 EF Core migration — plan §6:
  ```
  dotnet ef migrations add AddPasswordResetOtps --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
  ```
- [x] 3.4 `dotnet ef database update` against LocalDB (creates `PasswordResetOtps` physically) — plan §6
- [x] 3.5 Manual verification: `dotnet run --project apps/api/src/NoteManagement.Api`, then via curl/Swagger UI: register/login a user → call forgot-password → read the OTP from the console log → call reset-password with it → confirm `200` → confirm the old refresh token now fails on `/refresh` → confirm reusing the same OTP now returns `400` → confirm 5 wrong-code attempts lock the OTP even before requesting a new one

**Checkpoint (Phase 3):**
```bash
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet build apps/api/NoteManagement.sln
pnpm build
```

## Phase 4: Tests

One test per `specs/authentication/spec.md` scenario added by this ticket (12 scenarios), plus domain-rule and primitive tests. Plan §9 flags two testing-strategy decisions this phase must follow exactly:

1. `AuthServiceTests` (Unit) extends its existing hand-rolled-fake convention with `FakePasswordResetOtpRepository` and `FakeOtpGenerator` (incrementing-counter fake, same shape as `FakeRefreshTokenSecretService`), and passes `NullLogger<AuthService>.Instance` for the new `ILogger<AuthService>` parameter (no fake class needed).
2. `AuthControllerTests` (Integration) needs to know the raw OTP for any test that completes a full reset, but the code is only ever logged, never returned via HTTP. Tests that don't need the code (unknown-vs-registered-email parity, cooldown) use the existing `_factory`/`_client`. Tests that do use a **second** `WebApplicationFactory<Program>` (own isolated LocalDB database, same `UseSetting("ConnectionStrings:DefaultConnection", ...)` pattern) with `ConfigureTestServices` substituting `IOtpGenerator` for a private nested `SequentialOtpGenerator` fake that returns a pre-set, ordered sequence of codes — the same "swap one production seam for a deterministic test double" idea AB-1002's `BuildAccessToken` helper already used for expired/tampered JWTs.

| Spec scenario | Test |
|---|---|
| Registered email issues an OTP | `AuthServiceTests.ForgotPasswordAsync_WithRegisteredEmail_IssuesHashesAndLogsOtp` + `AuthControllerTests.ForgotPassword_WithRegisteredEmail_Returns200` |
| Unknown email gives the same generic response | `AuthServiceTests.ForgotPasswordAsync_WithUnknownEmail_DoesNothing` + `AuthControllerTests.ForgotPassword_WithUnknownAndRegisteredEmail_ReturnsIdenticalResponse` |
| New OTP invalidates the previous one | `AuthServiceTests.ForgotPasswordAsync_CalledTwiceOutsideCooldown_InvalidatesPreviousOtp` + `AuthControllerTests.ResetPassword_WithSupersededOtp_Returns400` (via `SequentialOtpGenerator`) |
| Repeat request within cooldown does not reissue an OTP | `AuthServiceTests.ForgotPasswordAsync_WithinCooldown_DoesNotIssueNewOtp` + `AuthControllerTests.ForgotPassword_CalledTwiceQuickly_ReturnsSame200BothTimes` |
| Successful password reset | `AuthServiceTests.ResetPasswordAsync_WithValidOtp_UpdatesPasswordAndMarksOtpUsed` + `AuthControllerTests.ResetPassword_WithValidOtp_Returns200` |
| Incorrect OTP rejected | `AuthServiceTests.ResetPasswordAsync_WithWrongOtp_ThrowsAndIncrementsAttemptCount` + `AuthControllerTests.ResetPassword_WithWrongOtp_Returns400` |
| Expired OTP rejected | `AuthServiceTests.ResetPasswordAsync_WithExpiredOtp_Throws` |
| Already-used OTP rejected | `AuthServiceTests.ResetPasswordAsync_WithAlreadyUsedOtp_Throws` + `AuthControllerTests.ResetPassword_CalledTwiceWithSameOtp_SecondCallReturns400` |
| OTP locked out after 5 incorrect attempts | `AuthServiceTests.ResetPasswordAsync_After5WrongAttempts_LocksOtpEvenWithCorrectCode` + `AuthControllerTests.ResetPassword_After5WrongAttempts_CorrectCodeSubsequentlyRejected` |
| Unknown email rejected with the same generic error | `AuthServiceTests.ResetPasswordAsync_WithUnknownEmail_ThrowsSameExceptionAsWrongOtp` |
| Password below policy rejected | `AuthControllerTests.ResetPassword_WithWeakNewPassword_Returns400` (DataAnnotations short-circuits before `AuthService`, same precedent as `Register_WithWeakPassword_Returns400`) |
| Successful reset revokes all sessions | `AuthServiceTests.ResetPasswordAsync_WithActiveRefreshTokens_RevokesAllOfThem` + `AuthControllerTests.ResetPassword_ThenRefreshWithOldToken_Returns401` |
| Successful reset invalidates other outstanding OTPs | Covered by the "New OTP invalidates the previous one" + "Successful password reset" rows above — no separate test |
| `PasswordResetOtp` domain rules | `PasswordResetOtpTests`: `IsActive_WhenNotUsedAndNotExpired_ReturnsTrue`, `IsActive_WhenUsed_ReturnsFalse`, `IsActive_WhenExpired_ReturnsFalse`, `RegisterFailedAttempt_BelowMaxAttempts_DoesNotInvalidate`, `RegisterFailedAttempt_ReachingMaxAttempts_Invalidates`, `Invalidate_WhenCalledTwice_KeepsFirstTimestamp` |
| `OtpGenerator` primitives | `OtpGeneratorTests`: `GenerateRawOtp_ProducesSixDigitCodes`, `Hash_IsDeterministic_AndDiffersFromRawOtp` |

- [x] 4.1 `Tests.Unit/Domain/PasswordResetOtpTests.cs`: the 6 `IsActive_*`/`RegisterFailedAttempt_*`/`Invalidate_*` tests listed above
- [x] 4.2 `Tests.Unit/Application/AuthServiceTests.cs` (extend): add `FakePasswordResetOtpRepository`, `FakeOtpGenerator` nested fakes; extend `CreateSut`'s optional params + pass `NullLogger<AuthService>.Instance`; add the 11 `ForgotPasswordAsync_*`/`ResetPasswordAsync_*` tests listed above
- [x] 4.3 `Tests.Integration/Infrastructure/OtpGeneratorTests.cs`: `GenerateRawOtp_ProducesSixDigitCodes`, `Hash_IsDeterministic_AndDiffersFromRawOtp`
- [x] 4.4 `Tests.Integration/Api/AuthControllerTests.cs` (extend): add the private nested `SequentialOtpGenerator` fake + a second `WebApplicationFactory<Program>` instance (own isolated LocalDB database, `IOtpGenerator` substituted via `ConfigureTestServices` — verified against `ConfigureServices` too; both work under `WebApplicationFactory<Program>`'s minimal-hosting interception, but `ConfigureTestServices` is kept as the intent-signaling, purpose-built API); add the 10 `ForgotPassword_*`/`ResetPassword_*` tests listed above (2 scenario rows above have no separate Integration test)

**Checkpoint (Phase 4 — final gate for this ticket, per `CLAUDE.md` Quality Gates, run in order):**
```bash
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
pnpm lint --max-warnings 0
pnpm build
pnpm test --coverage
```

## Not in scope for this ticket (plan §12)

Any frontend auth UI consuming these endpoints (AB-1010), rate limiting beyond the per-email 60-second reissue cooldown, any change to register/login/refresh/logout behavior beyond the 2 new `IAuthService` methods, any `Notes`/`Tags`/etc. change.
