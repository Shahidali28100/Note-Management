## Why

Users who forget their password currently have no way back into their account — `authentication` (AB-1002) only covers register/login/refresh/logout. FRS-AUTH-005/006 and the FRS §14 / SDS §92-93 ticket-traceability table assign forgot-password + OTP-based reset to **AB-1003**, next in the strict AB-1001 → AB-1016 dependency chain. This proposal closes that gap using the `PasswordResetOtps` table already scoped in SDS §12 / AGENTS.md §9, resolving the four "Open Technical Decisions" SDS §97 lists for AB-1003 (OTP expiration, OTP attempt limits, password policy, reset behavior) via user-approved answers below.

## What Changes

- **POST /api/auth/forgot-password** — accepts an `email`. Always responds `200 OK` with an identical generic message whether or not the email belongs to a registered user (FRS-AUTH-006: never reveal account existence).
  - When the email matches a user: generates a 6-digit numeric OTP, persists only its hash, sets a 10-minute expiry, invalidates any other still-outstanding OTP previously issued to that user (only the newest OTP is ever valid), and logs the raw OTP to the application console/logging system (no real email provider — AGENTS.md §11).
  - Per-email cooldown: a repeat request within 60 seconds of the last OTP actually issued to that email does **not** generate a new OTP (the existing one keeps its original expiry) — but still returns the same generic `200` response, so the cooldown itself never leaks whether the email exists.
- **POST /api/auth/reset-password** — accepts `email`, `otp`, and `newPassword`.
  - `newPassword` is validated against the same password policy as registration (≥8 chars, ≥1 letter, ≥1 digit — reusing `PasswordPolicyAttribute` from AB-1002).
  - The OTP must be unexpired, unused, matched to the given email, and not already locked out from too many incorrect attempts; any failure (wrong OTP, expired, already used, locked out, or unknown email) is rejected with the same generic `400 Bad Request` — the response never distinguishes which part was wrong (mirrors the login endpoint's non-enumerating `401` pattern from AB-1002).
  - Each incorrect OTP submitted against an otherwise-valid, unexpired OTP record increments an attempt counter; on the 5th incorrect attempt that OTP is locked (treated as used/invalid) and the user must request a new one via forgot-password.
  - On success: the user's password hash is updated, the OTP is marked used (single-use — FRS-AUTH-006), **all other outstanding OTPs for that user are invalidated**, and **all of the user's refresh tokens are revoked** (forces re-login on every device/session, the standard response to a credential change).
- New `PasswordResetOtp` EF Core entity + configuration + migration (SDS §12): `Id, UserId, OtpHash, ExpiresAt, UsedAt, CreatedAt`, plus an `AttemptCount` column (not in the original SDS §12 schema listing) needed to enforce the attempt-lockout behavior above — introduced here as part of this approved ticket spec, consistent with SDS §9 ("additional entities/columns only through an approved specification change").
- Both endpoints use the standard Problem Details error contract (SDS §39), same as every AB-1002 endpoint.

Out of scope for this ticket: any frontend auth UI (→ AB-1010); changes to register/login/refresh/logout (AB-1002, already shipped); rate limiting beyond the per-email 60s cooldown described above; real email delivery (never in scope per AGENTS.md §11).

## Capabilities

### New Capabilities
_None._

### Modified Capabilities
- `authentication`: adds forgot-password (OTP issuance) and password-reset (OTP redemption) requirements to the existing capability spec at `openspec/specs/authentication/spec.md`.

## Impact

- **DB**: new `PasswordResetOtps` table + EF Core migration under `apps/api/src/NoteManagement.Infrastructure/Migrations`, with an `AttemptCount int NOT NULL DEFAULT 0` column beyond the SDS §12 baseline (see "What Changes" above).
- **Domain**: `PasswordResetOtp` entity (`apps/api/src/NoteManagement.Domain`), no ASP.NET Core dependency.
- **Application**: forgot-password/reset-password use-cases added to (or alongside) `AuthService`, new DTOs (`ForgotPasswordRequestDto`, `ResetPasswordRequestDto`), validators, and a new `PasswordResetOtpRepository` interface.
- **Api**: two new actions on the existing `AuthController` (`ForgotPassword`, `ResetPassword`), both `[AllowAnonymous]`.
- **Shared TS contracts**: `ForgotPasswordRequest`/`ResetPasswordRequest` DTOs + mirrored Zod schemas added to `packages/shared` (AGENTS.md §12), for later consumption by AB-1010.
- **Downstream dependency**: none — this ticket only extends `authentication`; no other ticket depends on it beyond the frontend (AB-1010).
- **Process**: `delta-openapi.yaml` for these two endpoints is included in this change, required before `/plan` per AGENTS.md §8 / SDS §66.
