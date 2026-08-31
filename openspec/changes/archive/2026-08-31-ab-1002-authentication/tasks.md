# Tasks: ab-1002-authentication

Source: `proposal.md`, `plan.md`. Each task references the plan section it implements. `[PARALLEL]` tasks have no dependency on each other and may run in separate git worktrees per SDS §87.

## Phase 1: Foundation

Package/project-reference plumbing and pure data shapes first — nothing here has business logic yet, so it can all land before any service/repository implementation exists.

- [x] 1.1 `Directory.Packages.props`: add the 3 new `<PackageVersion>` rows (`Microsoft.AspNetCore.Authentication.JwtBearer 8.0.30`, `System.IdentityModel.Tokens.Jwt 8.22.0`, `Microsoft.Extensions.Identity.Core 8.0.30`) — plan §1
- [x] 1.2 Add the matching `<PackageReference>` (no inline version) to `NoteManagement.Api.csproj` (JwtBearer) and `NoteManagement.Infrastructure.csproj` (the other two) — plan §1/§2

**[PARALLEL] — 1.3 Domain vs. 1.4 Application DTOs/validation/exceptions/interfaces vs. 1.5 shared-package types, once 1.1–1.2 are done:**

- [x] 1.3 Domain entities — plan §3: `Entities/User.cs`, `Entities/RefreshToken.cs` (private setters + static factories, `RefreshToken.IsActive`/`Revoke()`)
- [x] 1.4 Application layer, data shapes only (no `AuthService` yet — plan §4):
  - `DTOs/Auth/RegisterRequestDto.cs`, `UserDto.cs`, `LoginRequestDto.cs`, `AuthTokensDto.cs`, `RefreshRequestDto.cs`, `LogoutRequestDto.cs`
  - `Validation/PasswordPolicyAttribute.cs`
  - `Exceptions/DuplicateEmailException.cs`, `InvalidCredentialsException.cs`, `InvalidRefreshTokenException.cs`
  - `Interfaces/IAuthService.cs`, `IUserRepository.cs`, `IRefreshTokenRepository.cs`, `IUnitOfWork.cs`, `IPasswordHasher.cs`, `IJwtTokenGenerator.cs`, `IRefreshTokenSecretService.cs`
- [x] 1.5 Shared package — plan §8: `packages/shared/package.json` (`"zod": "4.5.4"` dependency), `src/types/auth.ts`, `src/schemas/auth.ts`, export both from `src/index.ts`

**Checkpoint (Phase 1):**
```bash
dotnet build apps/api/NoteManagement.sln          # 0 errors — DTOs/interfaces/entities compile standalone
pnpm install && pnpm build                         # shared package (zod schemas) type-checks
```

## Phase 2: Core implementation

**[PARALLEL] — 2.1–2.6 Infrastructure vs. 2.7–2.8 Application service, once Phase 1 is done (the service only needs the *interfaces* from 1.4, not the Infrastructure implementations):**

- [x] 2.1 EF Core configurations — plan §5: `Configurations/UserConfiguration.cs` (unique `Email` index), `Configurations/RefreshTokenConfiguration.cs` (unique `TokenHash` index, composite `(UserId, RevokedAt)` index, cascade-delete FK to `Users`)
- [x] 2.2 `ApplicationDbContext` — plan §5: add `DbSet<User> Users`, `DbSet<RefreshToken> RefreshTokens`, `OnModelCreating` → `ApplyConfigurationsFromAssembly`
- [x] 2.3 `Data/UnitOfWork.cs` — plan §5: `IUnitOfWork` via `CreateExecutionStrategy().ExecuteAsync(...)` wrapping `BeginTransactionAsync`/`CommitAsync`
- [x] 2.4 Repositories — plan §5: `Repositories/UserRepository.cs`, `Repositories/RefreshTokenRepository.cs` (`RevokeAllActiveForUserAsync` via `ExecuteUpdateAsync` — no read-modify-write)
- [x] 2.5 Authentication primitives — plan §5: `Authentication/JwtOptions.cs`, `JwtTokenGenerator.cs` (`JwtSecurityTokenHandler`, HS256, `sub` claim), `RefreshTokenSecretService.cs` (`RandomNumberGenerator` + `SHA256`), `PasswordHasher.cs` (wraps `PasswordHasher<User>`)
- [x] 2.6 `Infrastructure/DependencyInjection.cs` — plan §5: register 2.3/2.4/2.5 (`AddScoped` for repos/UnitOfWork/PasswordHasher, `AddSingleton` for the stateless token/secret generators), build `JwtOptions` from config with the fail-fast `?? throw` style

- [x] 2.7 `Application/Services/AuthService.cs` — plan §4: `RegisterAsync`, `LoginAsync`, `RefreshAsync` (including the reuse-detection branch), `LogoutAsync`, all writes wrapped in `IUnitOfWork.RunInTransactionAsync`
- [x] 2.8 `Application/DependencyInjection.cs` — plan §4: register `IAuthService → AuthService`

**Checkpoint (Phase 2):**
```bash
dotnet build apps/api/NoteManagement.sln          # 0 errors — Infrastructure + Application compile (Api not wired yet)
```

## Phase 3: Integration

Wires controller → service → repository → DbContext → SQL Server, plus the JWT middleware and the migration that makes the tables exist.

- [x] 3.1 `Api/Controllers/AuthController.cs` — plan §7: `Register` (`[AllowAnonymous]`, 201), `Login`/`Refresh` (`[AllowAnonymous]`, 200), `Logout` (`[AllowAnonymous]`, 204), `GetMe` (**explicit `[Authorize]`** — no global fallback policy exists, so this must be on the action itself)
- [x] 3.2 `Api/Middleware/ProblemDetailsExceptionHandler.cs` — plan §7: `IExceptionHandler` mapping `DuplicateEmailException`→409, `InvalidCredentialsException`→401, `InvalidRefreshTokenException`→401; anything else falls through to the existing generic 500 handler
- [x] 3.3 `Program.cs` — plan §7: read `Jwt:SigningKey`/`Jwt:Issuer`/`Jwt:Audience` (fail-fast), `AddExceptionHandler<ProblemDetailsExceptionHandler>()`, `AddAuthentication().AddJwtBearer(...)` with full `TokenValidationParameters`, `AddAuthorization()`, add `app.UseAuthentication()` **before** the existing `app.UseAuthorization()`
- [x] 3.4 `appsettings.json` — plan §7: add non-secret `Jwt:Issuer`/`Jwt:Audience`. `appsettings.Development.json.example` — add `Jwt:SigningKey` placeholder with a comment. Local (gitignored) `appsettings.Development.json` — generate a random dev-only 256-bit signing key and fill it in, same treatment the LocalDB connection string already got in AB-1001
- [x] 3.5 EF Core migration — plan §6:
  ```
  dotnet ef migrations add AddUsersAndRefreshTokens --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
  ```
- [x] 3.6 `dotnet ef database update` against LocalDB (creates `Users`/`RefreshTokens` physically) — plan §6
- [x] 3.7 Manual verification: `dotnet run --project apps/api/src/NoteManagement.Api`, then via curl/Swagger UI walk register → login → GET `/api/auth/me` with the access token → refresh → confirm the old refresh token now fails → logout → confirm the refresh token now fails

**Checkpoint (Phase 3):**
```bash
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet build apps/api/NoteManagement.sln
pnpm build
```

## Phase 4: Tests

One test per `specs/authentication/spec.md` scenario (24 scenarios total). Infrastructure-layer crypto classes (`JwtTokenGenerator`, `PasswordHasher`, `RefreshTokenSecretService`) have no EF Core/DB dependency of their own but live in `NoteManagement.Infrastructure`, which `Tests.Unit` deliberately does not reference (AB-1001's graph) — so, matching the existing `ApplicationDbContextTests.cs` precedent, their tests live under `Tests.Integration/Infrastructure/` even though they don't touch the database or `WebApplicationFactory`. `AuthServiceTests` uses hand-rolled fakes for its 6 interface dependencies (same "no mocking library" convention as `FakeDatabaseHealthChecker`) rather than adding Moq/NSubstitute.

**Constructing an expired/tampered access token for `GetMe` tests** — `IJwtTokenGenerator` only ever issues a live, 15-minute token, so `GetMe_WithExpiredToken_Returns401` and `GetMe_WithTamperedToken_Returns401` cannot go through it. `AuthControllerTests.cs` instead gets a private helper that builds a raw JWT directly with `JwtSecurityTokenHandler` (from `System.IdentityModel.Tokens.Jwt`, already referenced transitively via `Infrastructure`/`Api`), independent of the production generator:

```csharp
private static string BuildAccessToken(Guid userId, DateTime expiresUtc, string? signingKeyOverride = null)
{
    var configuration = _factory.Services.GetRequiredService<IConfiguration>();
    var signingKey = signingKeyOverride ?? configuration["Jwt:SigningKey"]!;
    var credentials = new SigningCredentials(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: configuration["Jwt:Issuer"],
        audience: configuration["Jwt:Audience"],
        claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) },
        expires: expiresUtc,
        signingCredentials: credentials);

    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

- `GetMe_WithExpiredToken_Returns401` → `BuildAccessToken(userId, DateTime.UtcNow.AddMinutes(-1))` — real signing key (read from the test host's own `IConfiguration`, so it always matches whatever `Program.cs` validates against), `exp` in the past → `JwtBearer` rejects on `ValidateLifetime`.
- `GetMe_WithTamperedToken_Returns401` → `BuildAccessToken(userId, DateTime.UtcNow.AddMinutes(15), signingKeyOverride: "a-deliberately-wrong-signing-key-0123456789")` — valid (future) `exp`, wrong key → `JwtBearer` rejects on `ValidateIssuerSigningKey`/signature check, not lifetime, so this genuinely tests a different failure path than the expired-token test.
- `GetMe_WithValidToken_Returns200WithUserProfile` deliberately does **not** use this helper — it gets its access token from a real `POST /api/auth/login` call, so at least one `GetMe` test exercises the production `IJwtTokenGenerator` end-to-end.
- `GetMe_WithNoToken_Returns401` sends no `Authorization` header at all — no helper needed.

| Spec scenario | Test |
|---|---|
| Successful registration | 4.1 `AuthServiceTests.RegisterAsync_WithValidData_CreatesUser` + 4.6 `AuthControllerTests.Register_WithValidData_Returns201WithUser` |
| Duplicate email rejected | 4.1 `RegisterAsync_WithDuplicateEmail_ThrowsDuplicateEmailException` + 4.6 `Register_WithDuplicateEmail_Returns409` |
| Invalid email format rejected | 4.6 `Register_WithInvalidEmail_Returns400` (DataAnnotations short-circuits before `AuthService` — not unit-testable at the service level) |
| Password below policy rejected | 4.2 `PasswordPolicyAttributeTests.IsValid_WithPasswordMissingLetterOrDigitOrTooShort_ReturnsFalse` (Unit, direct) + 4.6 `Register_WithWeakPassword_Returns400` |
| Missing required field rejected | 4.6 `Register_WithMissingRequiredField_Returns400` |
| Successful login | 4.1 `LoginAsync_WithValidCredentials_ReturnsTokensAndPersistsHashedRefreshToken` (also covers "refresh token stored as a hash" — asserts the fake repo received a hash, not the raw generated token) + 4.6 `Login_WithValidCredentials_Returns200WithTokens` |
| Incorrect password rejected | 4.1 `LoginAsync_WithIncorrectPassword_ThrowsInvalidCredentialsException` + 4.6 `Login_WithIncorrectPassword_Returns401` |
| Unknown email rejected | 4.1 `LoginAsync_WithUnknownEmail_ThrowsInvalidCredentialsException` + 4.6 `Login_WithUnknownEmail_Returns401` |
| Concurrent sessions allowed | 4.6 `Login_CalledTwiceForSameUser_BothRefreshTokensRemainValid` (needs real persistence — Integration only) |
| Refresh token stored as a hash | covered by the "Successful login" row above — no separate test |
| Valid access token accepted / returns profile | 4.6 `AuthControllerTests.GetMe_WithValidToken_Returns200WithUserProfile` |
| Expired access token rejected | 4.6 `GetMe_WithExpiredToken_Returns401` |
| Tampered/invalidly signed token rejected | 4.6 `GetMe_WithTamperedToken_Returns401` |
| Missing credentials rejected | 4.6 `GetMe_WithNoToken_Returns401` |
| Valid refresh rotates tokens | 4.1 `RefreshAsync_WithValidToken_RotatesAndReturnsNewTokens` + 4.6 `Refresh_WithValidToken_Returns200AndOldTokenNoLongerWorks` |
| Expired refresh token rejected | 4.1 `RefreshAsync_WithExpiredToken_ThrowsInvalidRefreshTokenException` + 4.6 `Refresh_WithExpiredToken_Returns401` |
| Unknown refresh token rejected | 4.1 `RefreshAsync_WithUnknownToken_ThrowsInvalidRefreshTokenException` + 4.6 `Refresh_WithUnknownToken_Returns401` |
| Reused rotated token revokes all sessions | 4.1 `RefreshAsync_WithAlreadyRevokedToken_RevokesAllActiveSessionsForUserAndThrows` + 4.6 `Refresh_WithReusedRotatedToken_Returns401AndInvalidatesOtherActiveSession` |
| Sessions revoked by reuse detection cannot refresh | covered by `Refresh_WithReusedRotatedToken_Returns401AndInvalidatesOtherActiveSession` above (it asserts the other session's token subsequently fails too) — no separate test |
| Logout revokes the presented session | 4.1 `LogoutAsync_WithValidToken_RevokesToken` + 4.6 `Logout_WithValidToken_Returns204` |
| Revoked token cannot be refreshed | 4.6 `Logout_ThenRefreshWithSameToken_Returns401` |
| Logout does not affect other sessions | 4.6 `Logout_WithOneOfTwoSessions_OtherSessionRemainsValid` |
| `RefreshToken.IsActive`/`Revoke()` domain rules | 4.3 `RefreshTokenTests` (4 tests, see below) |

- [x] 4.1 `Tests.Unit/Application/AuthServiceTests.cs` (hand-rolled fakes for all 6 interfaces): the 11 `RegisterAsync_*`/`LoginAsync_*`/`RefreshAsync_*`/`LogoutAsync_*` tests listed in the table above
- [x] 4.2 `Tests.Unit/Application/PasswordPolicyAttributeTests.cs`: `IsValid_WithPasswordMissingLetterOrDigitOrTooShort_ReturnsFalse`, `IsValid_WithPasswordMeetingPolicy_ReturnsTrue`
- [x] 4.3 `Tests.Unit/Domain/RefreshTokenTests.cs`: `IsActive_WhenNotRevokedAndNotExpired_ReturnsTrue`, `IsActive_WhenRevoked_ReturnsFalse`, `IsActive_WhenExpired_ReturnsFalse`, `Revoke_WhenCalledTwice_KeepsFirstRevocationTimestamp`
- [x] 4.4 `Tests.Integration/Infrastructure/JwtTokenGeneratorTests.cs`: `GenerateAccessToken_ReturnsTokenWithSubClaimAndFifteenMinuteExpiry`
- [x] 4.5 `Tests.Integration/Infrastructure/PasswordHasherTests.cs` + `RefreshTokenSecretServiceTests.cs`: `Hash_ThenVerify_WithCorrectPassword_ReturnsTrue`, `Verify_WithIncorrectPassword_ReturnsFalse`, `GenerateRawToken_ProducesUniqueHighEntropyValues`, `Hash_IsDeterministic_AndDiffersFromRawToken`
- [x] 4.6 `Tests.Integration/Api/AuthControllerTests.cs` (`WebApplicationFactory<Program>`, real LocalDB, isolated test database per the `HealthEndpointTests` precedent): the 17 `Register_*`/`Login_*`/`GetMe_*`/`Refresh_*`/`Logout_*` tests listed in the table above

**Checkpoint (Phase 4 — final gate for this ticket, per `CLAUDE.md` Quality Gates, run in order):**
```bash
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
pnpm lint --max-warnings 0
pnpm build
pnpm test --coverage
```

## Not in scope for this ticket (plan §12)

Forgot-password/OTP/password reset (AB-1003), any `apps/web` UI, a global default-`[Authorize]` fallback policy, rate limiting/lockout, any `Notes`/`Tags`/etc. change.
