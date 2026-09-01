## Why

No authentication exists yet in the codebase. Every ticket after this one (Notes, Tags, Search, Sharing, Versions) requires a known, authenticated `UserId` to enforce per-user data isolation server-side (AGENTS.md §7, SDS §29, §58). AB-1002 is next in the strict AB-1001 → AB-1016 dependency chain (FRS §14, SDS §92) and establishes that foundation: registration, login, JWT access tokens, DB-backed refresh tokens, and logout.

Forgot-password / OTP-based reset (FRS-AUTH-005, FRS-AUTH-006) are grouped under "Authentication Requirements" in FRS §5, but the ticket-traceability table (FRS §14, SDS §93) assigns them to **AB-1003 (Password reset)**, not AB-1002. This proposal scopes strictly to register / login / refresh / logout and leaves forgot-password/reset-password to the next ticket.

## What Changes

- New `Users` and `RefreshTokens` EF Core entities, configurations, and migration (SDS §10, §11) — `Users.Email` unique, `RefreshTokens.TokenHash` stored (never the raw token), UTC timestamps throughout.
- **POST /api/auth/register** — creates a user account from name/email/password.
  - Password policy: minimum 8 characters, containing at least one letter and one digit.
  - Duplicate email → `409 Conflict`. Other validation failures → `400`.
  - Returns the created user only (`201`) — does **not** auto-login; the client calls `/api/auth/login` separately.
- **POST /api/auth/login** — validates email + password, returns a JWT access token (15 min TTL), a refresh token (7 day TTL), and access-token expiry info (FRS-AUTH-002).
  - Multiple concurrent sessions per user are allowed (e.g. phone + laptop); each login creates a new refresh-token row without revoking other active sessions.
- **POST /api/auth/refresh** — validates the submitted refresh token against its stored hash.
  - On success: rotates it — issues and persists a new refresh token, revokes the presented one, returns a new access token (single-use refresh tokens).
  - On reuse of an already-revoked/used token (theft signal): revokes **all** of that user's active refresh tokens and rejects the request with `401`.
  - Expired/unknown/invalid tokens are rejected with `401`.
- **POST /api/auth/logout** — revokes only the refresh token presented in the request; the user's other active sessions are unaffected (consistent with the multi-device model above).
- **GET /api/auth/me** — `[Authorize]`-protected; returns the current user's id/name/email from the access token's `sub` claim. Added so the JWT-validation behavior (below) has a real endpoint to prove itself against end-to-end, since the other four endpoints are all anonymous by nature.
- JWT access tokens are signed with **HS256** (shared secret from configuration, never committed — SDS §64). `sub` claim carries `UserId`. Signature, expiration, issuer, and audience are validated on every authenticated request (SDS §27).
- Passwords are hashed with a secure, salted algorithm appropriate for ASP.NET Core; hashes are never returned in any API response or logged (SDS §61, §63).
- All four endpoints use the standard Problem Details error contract (`type`, `title`, `status`, `detail`) per SDS §39.

Out of scope for this ticket: forgot-password, OTP generation/validation, password reset (→ AB-1003); any frontend auth UI (→ AB-1010); Notes/Tags/Search/Share/Version endpoints and their `[Authorize]` usage beyond the shared middleware this ticket establishes.

## Capabilities

### New Capabilities
- `authentication`: user registration, password-based login, JWT access-token issuance/validation, refresh-token lifecycle (issuance, rotation, reuse detection, multi-device sessions, logout revocation).

### Modified Capabilities
_None — no `openspec/specs/` capabilities exist yet; this is the first ticket to define one._

## Impact

- **DB**: new `Users`, `RefreshTokens` tables + EF Core migration under `apps/api/src/NoteManagement.Infrastructure/Migrations`.
- **Domain**: `User`, `RefreshToken` entities (`apps/api/src/NoteManagement.Domain`), no ASP.NET Core dependency.
- **Application**: auth service/use-cases, DTOs (`RegisterRequest`, `LoginRequest`, `TokenResponse`, `RefreshRequest`, `LogoutRequest`), validators (`apps/api/src/NoteManagement.Application`).
- **Api**: `AuthController`, JWT bearer authentication configuration in startup, `appsettings` JWT config keys (signing secret, issuer, audience, access/refresh TTLs) (`apps/api/src/NoteManagement.Api`).
- **Shared TS contracts**: Auth DTOs + mirrored Zod schemas added to `packages/shared` (AGENTS.md §12) for later consumption by AB-1010 (frontend auth pages).
- **Downstream dependency**: every subsequent private-resource ticket (AB-1004 onward) relies on the `[Authorize]` middleware and `sub`-claim convention established here.
- **Process**: a `delta-openapi.yaml` covering these four endpoints is required before `/plan` per AGENTS.md §8 / SDS §66.
