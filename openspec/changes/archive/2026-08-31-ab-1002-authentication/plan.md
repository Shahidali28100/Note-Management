# Plan: ab-1002-authentication

Source: `proposal.md`, `specs/authentication/spec.md`, `delta-openapi.yaml`, `docs/SDS.md`, `AGENTS.md`, `apps/api/CLAUDE.md`. Mirrors the layering/DI/testing template AB-1001 already established (`HealthController → IHealthCheckService → IDatabaseHealthChecker → DatabaseHealthChecker`) rather than inventing a new one.

## 0. Reused facts from AB-1001 (already resolved, not re-litigated)

- `.NET SDK`/LocalDB/`dotnet-ef` environment — already verified working; `.config/dotnet-tools.json` pins `dotnet-ef 8.0.30`, matching the `Microsoft.EntityFrameworkCore.*` 8.0.30 packages already in `Directory.Packages.props`.
- Central Package Management (`apps/api/Directory.Packages.props`) — new NuGet packages are added there, never with an inline `Version=` on the `<PackageReference>`.
- `packages/shared` has no build step (raw TS source, `tsc --noEmit` only) — new files just need to type-check and be exported from `src/index.ts`.

## 1. New package versions (live-verified via nuget.org / npmjs registry — none from memory)

| Package | Version | Where | Role |
|---|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | `8.0.30` | `NoteManagement.Api` | JWT bearer authentication middleware (validates incoming access tokens). Pinned to the same 8.0.30 patch already used for EF Core / `dotnet-ef`, not the newer 10.x line — stays on the net8.0-aligned branch this project targets. |
| System.IdentityModel.Tokens.Jwt | `8.22.0` | `NoteManagement.Infrastructure` | `JwtSecurityTokenHandler` — used to *issue* (sign) access tokens. Versioned independently of ASP.NET Core itself; 8.22.0 is current stable and net8.0-compatible. |
| Microsoft.Extensions.Identity.Core | `8.0.30` | `NoteManagement.Infrastructure` | Brings in `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` (PBKDF2-HMACSHA256) without pulling in the full ASP.NET Core Identity membership/UI system — just the hasher primitive. |
| zod | `4.5.4` | `packages/shared` | Runtime validation schemas mirrored from the backend DTOs (AGENTS.md §12). Zod 4 has been the mainline release for over a year as of this ticket — no reason to pin an older major the way AB-1001 deliberately did for just-released majors (Vite 8, TS 7). |

No new package is needed for password/refresh-token validation logic itself — `System.Security.Cryptography.RandomNumberGenerator` and `SHA256` (raw token generation + hashing) are in the BCL already available to `NoteManagement.Infrastructure`. No FluentValidation: request-DTO validation uses `System.ComponentModel.DataAnnotations` (already free via `[ApiController]`'s automatic model-validation filter, which already returns a `400` `ValidationProblemDetails` — no new dependency, no hand-written 400 branches).

## 2. File tree to create / modify

```
apps/api/
├── Directory.Packages.props                                  # MODIFIED — add 3 NuGet rows from §1
├── src/
│   ├── NoteManagement.Domain/
│   │   └── Entities/
│   │       ├── User.cs                                        (NEW)
│   │       └── RefreshToken.cs                                (NEW)
│   │
│   ├── NoteManagement.Application/
│   │   ├── DTOs/Auth/
│   │   │   ├── RegisterRequestDto.cs                           (NEW)
│   │   │   ├── UserDto.cs                                      (NEW)
│   │   │   ├── LoginRequestDto.cs                              (NEW)
│   │   │   ├── AuthTokensDto.cs                                (NEW)
│   │   │   ├── RefreshRequestDto.cs                            (NEW)
│   │   │   └── LogoutRequestDto.cs                             (NEW)
│   │   ├── Validation/
│   │   │   └── PasswordPolicyAttribute.cs                      (NEW — DataAnnotations ValidationAttribute)
│   │   ├── Exceptions/
│   │   │   ├── DuplicateEmailException.cs                      (NEW)
│   │   │   ├── InvalidCredentialsException.cs                  (NEW)
│   │   │   └── InvalidRefreshTokenException.cs                 (NEW)
│   │   ├── Interfaces/
│   │   │   ├── IAuthService.cs                                 (NEW)
│   │   │   ├── IUserRepository.cs                              (NEW)
│   │   │   ├── IRefreshTokenRepository.cs                      (NEW)
│   │   │   ├── IUnitOfWork.cs                                  (NEW)
│   │   │   ├── IPasswordHasher.cs                               (NEW)
│   │   │   ├── IJwtTokenGenerator.cs                            (NEW)
│   │   │   └── IRefreshTokenSecretService.cs                   (NEW)
│   │   ├── Services/
│   │   │   └── AuthService.cs                                  (NEW)
│   │   ├── NoteManagement.Application.csproj                   # MODIFIED — no new package refs needed (DataAnnotations is BCL)
│   │   └── DependencyInjection.cs                               # MODIFIED — register IAuthService
│   │
│   ├── NoteManagement.Infrastructure/
│   │   ├── Configurations/
│   │   │   ├── UserConfiguration.cs                             (NEW)
│   │   │   └── RefreshTokenConfiguration.cs                     (NEW)
│   │   ├── Data/
│   │   │   ├── ApplicationDbContext.cs                          # MODIFIED — add DbSets + OnModelCreating
│   │   │   └── UnitOfWork.cs                                    (NEW)
│   │   ├── Repositories/
│   │   │   ├── UserRepository.cs                                (NEW)
│   │   │   └── RefreshTokenRepository.cs                        (NEW)
│   │   ├── Authentication/
│   │   │   ├── JwtOptions.cs                                    (NEW — internal POCO, not exposed to Application)
│   │   │   ├── JwtTokenGenerator.cs                              (NEW)
│   │   │   ├── RefreshTokenSecretService.cs                      (NEW)
│   │   │   └── PasswordHasher.cs                                 (NEW — wraps Identity's PasswordHasher<User>)
│   │   ├── Migrations/
│   │   │   └── <timestamp>_AddUsersAndRefreshTokens.cs          (NEW — via `dotnet ef migrations add`)
│   │   ├── NoteManagement.Infrastructure.csproj                  # MODIFIED — add 2 package refs from §1
│   │   └── DependencyInjection.cs                                 # MODIFIED — register repositories, UnitOfWork, JWT/password/refresh-token services
│   │
│   └── NoteManagement.Api/
│       ├── Controllers/
│       │   └── AuthController.cs                                 (NEW)
│       ├── Middleware/
│       │   └── ProblemDetailsExceptionHandler.cs                  (NEW — IExceptionHandler)
│       ├── NoteManagement.Api.csproj                              # MODIFIED — add JwtBearer package ref from §1
│       ├── Program.cs                                              # MODIFIED — JWT bearer wiring, UseAuthentication, register the exception handler
│       ├── appsettings.json                                        # MODIFIED — add non-secret Jwt:Issuer/Jwt:Audience
│       └── appsettings.Development.json.example                    # MODIFIED — add Jwt:SigningKey placeholder
│
└── tests/
    ├── NoteManagement.Tests.Unit/
    │   ├── Domain/
    │   │   └── RefreshTokenTests.cs                               (NEW)
    │   └── Application/
    │       └── AuthServiceTests.cs                                (NEW)
    └── NoteManagement.Tests.Integration/
        └── Api/
            └── AuthControllerTests.cs                              (NEW)

packages/shared/
├── package.json                                                    # MODIFIED — add zod dependency
└── src/
    ├── types/auth.ts                                                (NEW)
    ├── schemas/auth.ts                                               (NEW)
    └── index.ts                                                       # MODIFIED — export the two new files
```

## 3. Domain layer

Plain POCOs, zero ASP.NET Core/EF Core dependency (`NoteManagement.Domain.csproj` gets no new package references). Private setters + a static factory keep invariants at the construction site instead of scattering `new User { ... }` object-initializers around the Application layer; a private parameterless constructor exists only for EF Core materialization.

```csharp
// Domain/Entities/User.cs
public sealed class User
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private User() { }

    public static User Register(string name, string email, string passwordHash)
    {
        var now = DateTime.UtcNow;
        return new User { Id = Guid.NewGuid(), Name = name, Email = email, PasswordHash = passwordHash, CreatedAt = now, UpdatedAt = now };
    }
}

// Domain/Entities/RefreshToken.cs
public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Issue(Guid userId, string tokenHash, DateTime expiresAt) =>
        new() { Id = Guid.NewGuid(), UserId = userId, TokenHash = tokenHash, ExpiresAt = expiresAt, CreatedAt = DateTime.UtcNow };

    /// <summary>Domain rule used by both the refresh and reuse-detection flows: a token is usable only if it hasn't been revoked and hasn't naturally expired.</summary>
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;

    /// <summary>Idempotent — revoking an already-revoked token does not move its RevokedAt timestamp.</summary>
    public void Revoke() => RevokedAt ??= DateTime.UtcNow;
}
```

These exact 6 columns per entity match AGENTS.md §9 / SDS §10-§11 literally — no extra audit columns (e.g. no "replaced-by" pointer), since the approved schema lists exactly `Id, UserId, TokenHash, ExpiresAt, RevokedAt, CreatedAt` for `RefreshTokens`.

## 4. Application layer

**DTOs** (`sealed record`s, matching `delta-openapi.yaml` field-for-field):

```csharp
// Validation attributes target the constructor parameters directly (no "property:" prefix).
// Discovered during implementation: ASP.NET Core's record model-binding validation reads
// metadata from the primary constructor parameters, not the compiler-generated properties —
// "property:"-targeted attributes are silently ignored at compile time and throw an
// InvalidOperationException ("...validation metadata must be associated with the constructor
// parameter") the first time the action actually runs.
public sealed record RegisterRequestDto(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, MinLength(8), PasswordPolicy] string Password);

public sealed record UserDto(Guid Id, string Name, string Email);

public sealed record LoginRequestDto(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public sealed record AuthTokensDto(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc, string TokenType = "Bearer");

public sealed record RefreshRequestDto([Required] string RefreshToken);

public sealed record LogoutRequestDto([Required] string RefreshToken);
```

`PasswordPolicyAttribute : ValidationAttribute` fails validation unless the string contains at least one letter (`char.IsLetter`) and at least one digit (`char.IsDigit`) — the `[MinLength(8)]` attribute covers the length half of the policy. Both run automatically via `[ApiController]`'s model-validation filter → `400` `ValidationProblemDetails`, no manual checks in the controller or service.

**Interfaces** (implemented in Infrastructure, same inversion shape as `IDatabaseHealthChecker`):

```csharp
public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    void Add(User user);
}

public interface IRefreshTokenRepository
{
    void Add(RefreshToken token);
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct);
    Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken ct); // atomic bulk UPDATE, see §6
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
    Task RunInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface IJwtTokenGenerator
{
    (string AccessToken, DateTime ExpiresAtUtc) GenerateAccessToken(Guid userId);
}

public interface IRefreshTokenSecretService
{
    string GenerateRawToken();      // cryptographically random, base64url, 256 bits of entropy
    string Hash(string rawToken);   // SHA-256, hex — used both to store and to look up by hash
}
```

**`AuthService : IAuthService`** — the one Application-layer orchestrator, thin repositories/generators wired together:

```csharp
public sealed class AuthService : IAuthService
{
    // ctor takes: IUserRepository, IRefreshTokenRepository, IUnitOfWork,
    // IPasswordHasher, IJwtTokenGenerator, IRefreshTokenSecretService

    public async Task<UserDto> RegisterAsync(RegisterRequestDto request, CancellationToken ct)
    {
        var passwordHash = _passwordHasher.Hash(request.Password);
        var user = User.Register(request.Name, request.Email, passwordHash);

        await _unitOfWork.RunInTransactionAsync(async token =>
        {
            _userRepository.Add(user);
            try
            {
                await _unitOfWork.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex) when (IsUniqueEmailViolation(ex))
            {
                throw new DuplicateEmailException(request.Email);
            }
        }, ct);

        return new UserDto(user.Id, user.Name, user.Email);
    }

    // LoginAsync: lookup by email -> IPasswordHasher.Verify -> same InvalidCredentialsException
    // for "not found" and "wrong password" (never reveals which — spec scenario) -> issue tokens.

    // RefreshAsync: hash presented token -> GetByTokenHashAsync ->
    //   not found                => throw InvalidRefreshTokenException (no cascade)
    //   found, !IsActive because revoked => RevokeAllActiveForUserAsync(token.UserId) THEN throw InvalidRefreshTokenException
    //   found, !IsActive because expired  => throw InvalidRefreshTokenException (no cascade)
    //   found, IsActive       => token.Revoke(); add new RefreshToken; SaveChangesAsync once (both
    //                            tracked on the same context — atomic without a second round trip);
    //                            issue new access token.

    // LogoutAsync: hash presented token -> GetByTokenHashAsync ->
    //   not found or !IsActive => throw InvalidRefreshTokenException
    //   IsActive               => token.Revoke(); SaveChangesAsync.
}
```

Relying on the `Users.Email` unique index (§5) plus catching `DbUpdateException` — rather than a pre-check `ExistsByEmailAsync` — avoids a check-then-insert race where two concurrent registrations with the same email could both pass a pre-check and then both attempt to insert.

`RunInTransactionAsync` wraps every write here per AGENTS.md §6 ("auth writes... run inside an EF Core transaction"), even for the single-insert cases (Register, Login) where EF Core's own single-`SaveChangesAsync` atomicity would already be sufficient on its own — being explicit and uniform across all `AuthService` writes is cheaper than reasoning about which ones technically need it, and it establishes the exact transactional-wrapper shape AB-1004's note-save+version-snapshot transaction will reuse.

## 5. Infrastructure layer

**EF Core configurations** (`IEntityTypeConfiguration<T>`, applied via `modelBuilder.ApplyConfigurationsFromAssembly` in `ApplicationDbContext.OnModelCreating` — new override, none existed before since AB-1001 had zero entities):

- `UserConfiguration`: `ToTable("Users")`, `Email` — `HasMaxLength(320)`, `IsRequired()`, unique index; `Name` — `HasMaxLength(200)`, required; `PasswordHash` — `HasMaxLength(500)`, required (headroom above the ~84-char PBKDF2 hash `PasswordHasher<T>` actually produces).
- `RefreshTokenConfiguration`: `ToTable("RefreshTokens")`, `TokenHash` — `HasMaxLength(128)` (SHA-256 hex is 64 chars — headroom kept), **unique index** (the primary lookup path on every `/refresh` and `/logout` call); composite index on `(UserId, RevokedAt)` (the reuse-detection cascade's `WHERE UserId = X AND RevokedAt IS NULL` query); FK to `Users.Id` with `OnDelete(DeleteBehavior.Cascade)` (a refresh token is meaningless once its user is gone — no soft-delete concept exists for `Users`, unlike `Notes`).

**`ApplicationDbContext`** gains:
```csharp
public DbSet<User> Users => Set<User>();
public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
```

**`UnitOfWork : IUnitOfWork`** — uses EF Core's `IExecutionStrategy` (`CreateExecutionStrategy().ExecuteAsync(...)`) around `BeginTransactionAsync`/`CommitAsync`, not a bare `using var tx = ...`, because SQL Server's default execution strategy can be configured with connection retries and a manual transaction defeats automatic retry unless wrapped this way — cheap to get right now, expensive to retrofit once retry policies are added later.

**`RefreshTokenRepository.RevokeAllActiveForUserAsync`** uses EF Core 8's `ExecuteUpdateAsync` (a single atomic `UPDATE ... SET RevokedAt = @now WHERE UserId = @id AND RevokedAt IS NULL`), not a load-then-mutate-then-`SaveChanges` loop — matches the same "atomic, no read-modify-write" principle SDS §47 requires for `ShareLinks.ViewCount`, applied here to the reuse-detection cascade.

**`PasswordHasher : IPasswordHasher`** wraps `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (PBKDF2-HMACSHA256, ASP.NET Core's own default v3 hash format) — no hand-rolled crypto.

**`JwtTokenGenerator : IJwtTokenGenerator`** uses `JwtSecurityTokenHandler` (`System.IdentityModel.Tokens.Jwt`), `SymmetricSecurityKey` + `SigningCredentials(..., SecurityAlgorithms.HmacSha256)`, single claim `sub = userId`, 15-minute lifetime, `Issuer`/`Audience` from `JwtOptions` (an Infrastructure-internal record — Application never sees it, only the `IJwtTokenGenerator` interface).

**`RefreshTokenSecretService`**: `GenerateRawToken()` → `RandomNumberGenerator.GetBytes(32)` → `Convert.ToBase64String` (URL-safe variant); `Hash(raw)` → `SHA256.HashData(Encoding.UTF8.GetBytes(raw))` → lowercase hex string. The raw value is what the client receives and must present back; only the hash is ever persisted (spec's "Refresh Token Issuance and Storage" requirement).

**`DependencyInjection.cs`** (Infrastructure) additions: `AddScoped<IUserRepository, UserRepository>()`, `AddScoped<IRefreshTokenRepository, RefreshTokenRepository>()`, `AddScoped<IUnitOfWork, UnitOfWork>()`, `AddScoped<IPasswordHasher, PasswordHasher>()`, `AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>()` (stateless once `JwtOptions` is built from config), `AddSingleton<IRefreshTokenSecretService, RefreshTokenSecretService>()`. `JwtOptions` itself is built once from `IConfiguration` inside `AddInfrastructure`, using the same fail-fast `?? throw new InvalidOperationException(...)` style already used for `ConnectionStrings:DefaultConnection`.

## 6. EF Core migration

- **Name**: `AddUsersAndRefreshTokens`
- **Command**:
  ```bash
  dotnet ef migrations add AddUsersAndRefreshTokens \
    --project apps/api/src/NoteManagement.Infrastructure \
    --startup-project apps/api/src/NoteManagement.Api

  dotnet ef database update \
    --project apps/api/src/NoteManagement.Infrastructure \
    --startup-project apps/api/src/NoteManagement.Api
  ```
- **Entity changes**: adds `Users` and `RefreshTokens` tables, the `Users.Email` unique index, the `RefreshTokens.TokenHash` unique index, the `RefreshTokens.(UserId, RevokedAt)` composite index, and the `RefreshTokens.UserId → Users.Id` FK (cascade delete).
- **Backward compatible**: yes — purely additive on top of `AB-1001`'s empty `InitialCreate` migration; no existing table/column is touched.

## 7. Api layer

**`AuthController`** — every action `[AllowAnonymous]` except `GetMe`. Register/login/refresh/logout are all entry points that precede having a live access token; even logout treats **possession of the still-valid refresh token itself** as its credential (so a client whose 15-minute access token already expired can still log out cleanly without refreshing first) — the service layer still rejects an unknown/already-revoked token with `401`.

```csharp
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpPost("register")]  [AllowAnonymous]
    public async Task<ActionResult<UserDto>> Register(RegisterRequestDto request, CancellationToken ct)
    {
        var user = await _authService.RegisterAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, user); // no GET-by-id endpoint exists to CreatedAtAction against
    }

    [HttpPost("login")]    [AllowAnonymous] public Task<ActionResult<AuthTokensDto>> Login(LoginRequestDto request, CancellationToken ct) => ...; // 200 Ok(tokens)
    [HttpPost("refresh")]  [AllowAnonymous] public Task<ActionResult<AuthTokensDto>> Refresh(RefreshRequestDto request, CancellationToken ct) => ...; // 200 Ok(tokens)
    [HttpPost("logout")]   [AllowAnonymous] public async Task<IActionResult> Logout(LogoutRequestDto request, CancellationToken ct) { await _authService.LogoutAsync(request, ct); return NoContent(); }

    [HttpGet("me")]
    [Authorize] // explicit — ASP.NET Core endpoints are anonymous by default; no global FallbackPolicy is configured in Program.cs (§7), so omitting this would let GetMe run unauthenticated and every JWT-validation test against it would pass for the wrong reason
    public ActionResult<UserDto> GetMe()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        // -> _authService (or a small IUserRepository read) to load + map to UserDto; 404 is not
        //    reachable here in practice (a valid token's sub always maps to a real user), but the
        //    lookup still goes through the repository rather than trusting claims blindly.
    }
}
```

No try/catch in the controller. **`ProblemDetailsExceptionHandler : IExceptionHandler`** (registered via `builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>()`, ahead of the existing `AddProblemDetails()`) maps:
- `DuplicateEmailException` → `409`
- `InvalidCredentialsException` → `401`
- `InvalidRefreshTokenException` → `401`
- anything else → not handled here, falls through to the generic `UseExceptionHandler()` → `500` Problem Details already established in AB-1001.

This is a reusable piece of plumbing, not an auth-only one: AB-1004+ will add their own typed exceptions (`NotFoundException`, `ForbiddenException`, ...) to the same handler's mapping rather than each controller hand-rolling try/catch.

**`Program.cs`** additions (JWT signature/issuer/audience/lifetime validated on every request per spec):
```csharp
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Configuration 'Jwt:SigningKey' not found. Copy appsettings.Development.json.example to appsettings.Development.json and fill it in.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Configuration 'Jwt:Issuer' not found.");
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Configuration 'Jwt:Audience' not found.");

builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
    // Discovered during implementation: without this, ASP.NET Core remaps the standard "sub"
    // claim to the legacy ClaimTypes.NameIdentifier URI, so GetMe's literal
    // JwtRegisteredClaimNames.Sub lookup (and every future [Authorize] action's) finds nothing.
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwtIssuer,
        ValidateAudience = true, ValidAudience = jwtAudience,
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
    };
    });
builder.Services.AddAuthorization();
// ...
app.UseAuthentication();   // NEW — must precede UseAuthorization()
app.UseAuthorization();    // already existed
```
`Jwt:Issuer`/`Jwt:Audience` are not secrets — added to the committed `appsettings.json` (e.g. `"NoteManagementApi"` / `"NoteManagementClient"`). `Jwt:SigningKey` is a secret — added only to the gitignored `appsettings.Development.json` (a random 256-bit key generated during implementation, same treatment the LocalDB connection string already gets) and documented as a placeholder in the committed `appsettings.Development.json.example`.

**Deliberate simplicity**: `Program.cs` reads the 3 `Jwt:*` keys directly from `IConfiguration` for `AddJwtBearer`, while `Infrastructure/DependencyInjection.cs` separately reads the same 3 keys to build `JwtOptions` for the token *generator*. This is a small, intentional duplication rather than introducing an `IOptions<JwtOptions>` binding (and its `Microsoft.Extensions.Options.ConfigurationExtensions` package) across a layer boundary that doesn't otherwise need it — both reads are one-line, fail-fast, and only 3 keys are involved.

## 8. Shared TS contracts (`packages/shared`)

`src/types/auth.ts` — plain TS interfaces mirroring §4's DTOs field-for-field (`RegisterRequest`, `UserResponse`, `LoginRequest`, `AuthTokensResponse`, `RefreshRequest`, `LogoutRequest`), camelCase matching `delta-openapi.yaml`.

`src/schemas/auth.ts` — Zod schemas per type (`registerRequestSchema`, `loginRequestSchema`, etc.), each type re-derived via `z.infer<>` rather than hand-duplicating the interface (per `packages/shared/CLAUDE.md` step 5). Password policy mirrored as a UX convenience only (`.min(8).regex(/[A-Za-z]/).regex(/[0-9]/)`) — the backend's `PasswordPolicyAttribute` remains the actual authority.

Both files exported from `src/index.ts`. `packages/shared/package.json` gains `"dependencies": { "zod": "4.5.4" }`.

## 9. Reuse of existing code / patterns

- **Layering + DI-per-layer registration** (`AddApplication()`, `AddInfrastructure(configuration)`) — reused as-is, only extended.
- **Fail-fast config style** (`?? throw new InvalidOperationException(...)`) — reused verbatim for the 3 new `Jwt:*` keys.
- **`AddProblemDetails()` / `UseExceptionHandler()`** already in `Program.cs` — extended (not replaced) by registering `ProblemDetailsExceptionHandler` ahead of it.
- **`[ApiController]` automatic model validation** — reused for all `400` cases; no new validation library.
- Nothing in `packages/shared` or `apps/web` exists yet to reuse (both untouched by AB-1002 beyond the shared-types addition) — AB-1010 is what actually consumes these.

## 10. Checkpoint commands

Run in order; fix and re-run on first failure before moving to the next (`CLAUDE.md` Quality Gates).

**Backend** (after §3–§7 file changes):
```bash
dotnet ef migrations add AddUsersAndRefreshTokens --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
```

**Frontend/shared** (after §8 file changes — `packages/shared` only, no `apps/web` changes this ticket, but the root `pnpm build`/`test` graph still needs to stay green):
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
| Successful registration / duplicate / invalid email / weak password / missing field | `AuthServiceTests.RegisterAsync_*` (Unit) + `AuthControllerTests.Register_*` (Integration, real LocalDB unique-constraint path) |
| Successful login / incorrect password / unknown email (generic error) | `AuthServiceTests.LoginAsync_*` (Unit, faked repo+hasher) + `AuthControllerTests.Login_*` (Integration) |
| Valid/expired/tampered/missing access token; `/me` returns profile | `AuthControllerTests.GetMe_*` (Integration — real `WebApplicationFactory<Program>` pipeline, the only way to exercise real `JwtBearer` middleware behavior) |
| Refresh token stored as hash | `RefreshTokenRepositoryTests` or asserted inline in `AuthServiceTests.RegisterAsync_.../LoginAsync_...` via a fake repository capturing the entity passed to `Add` |
| Valid refresh rotates / expired rejected / unknown rejected | `AuthServiceTests.RefreshAsync_*` (Unit) + `AuthControllerTests.Refresh_*` (Integration) |
| Reused rotated token revokes all sessions / revoked sessions can't refresh | `AuthServiceTests.RefreshAsync_WithAlreadyRevokedToken_RevokesAllActiveSessionsForUser` (Unit) + one Integration test proving a second, still-valid session's token stops working after the cascade |
| Logout revokes presented session / revoked token can't refresh / other sessions unaffected | `AuthServiceTests.LogoutAsync_*` (Unit) + `AuthControllerTests.Logout_*` (Integration) |
| `RefreshToken.IsActive` / `Revoke()` domain rules | `RefreshTokenTests` (Unit, Domain — no infrastructure, matches AB-1001's Domain-purity intent) |

Every named test follows `Method_Condition_ExpectedResult` (AGENTS.md §6/§10).

## 12. Explicitly not doing in this ticket

- Forgot-password, OTP issuance/validation, password reset (AB-1003).
- Any frontend auth UI/pages consuming these endpoints (AB-1010) — `packages/shared` additions are the only cross-boundary artifact this ticket produces for that later ticket.
- A global default-`[Authorize]` policy for all future controllers — each ticket's controller opts in explicitly, same as `HealthController` opted out explicitly.
- Rate limiting / brute-force lockout on login or refresh — not required by FRS/SDS for this ticket; would need its own spec discussion if wanted.
- Any change to `Notes`/`Tags`/etc. — out of scope, AB-1004+.
