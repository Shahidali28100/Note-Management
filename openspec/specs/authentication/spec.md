# authentication Specification

## Purpose
Establishes how a user proves their identity to the system: registration, password-based login, JWT access tokens, and the DB-backed refresh-token lifecycle that keeps sessions alive without requiring re-login every 15 minutes.

## Requirements

### Requirement: User Registration
The system SHALL allow a new, unauthenticated visitor to register an account by providing a name, an email address, and a password.

The email address SHALL be unique across all users. The password SHALL be at least 8 characters long and SHALL contain at least one letter and at least one digit.

A successful registration SHALL create a new user account and SHALL respond with the created user's non-sensitive profile fields (not a password, password hash, or any token). Registration SHALL NOT automatically issue an access or refresh token; the client authenticates via a subsequent login call.

#### Scenario: Successful registration
- **WHEN** a visitor submits a name, a unique valid email, and a password meeting the policy
- **THEN** the system creates a new user account and responds `201 Created` with the user's id, name, and email (no password, hash, or tokens)

#### Scenario: Duplicate email rejected
- **WHEN** a visitor submits an email that already belongs to an existing user
- **THEN** the system rejects the request with `409 Conflict` and does not create a new account

#### Scenario: Invalid email format rejected
- **WHEN** a visitor submits a malformed email address
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Password below policy rejected
- **WHEN** a visitor submits a password shorter than 8 characters, or missing a letter, or missing a digit
- **THEN** the system rejects the request with `400 Bad Request`

#### Scenario: Missing required field rejected
- **WHEN** a visitor submits a request missing name, email, or password
- **THEN** the system rejects the request with `400 Bad Request`

### Requirement: User Login
The system SHALL allow a registered user to authenticate using their email and password.

A successful login SHALL respond with a JWT access token, a refresh token, and access-token expiration information. The access token SHALL expire 15 minutes after issuance. The refresh token SHALL expire 7 days after issuance.

Logging in SHALL NOT invalidate the user's other existing, still-valid refresh tokens — a user may hold multiple concurrent sessions (e.g. one per device).

#### Scenario: Successful login
- **WHEN** a registered user submits their correct email and password
- **THEN** the system responds `200 OK` with a JWT access token, a refresh token, and the access token's expiration time

#### Scenario: Incorrect password rejected
- **WHEN** a request submits a registered email with an incorrect password
- **THEN** the system rejects the request with `401 Unauthorized` and does not reveal whether the password or the email was the invalid part

#### Scenario: Unknown email rejected
- **WHEN** a request submits an email with no matching account
- **THEN** the system rejects the request with `401 Unauthorized` using the same generic error as an incorrect password

#### Scenario: Concurrent sessions allowed
- **WHEN** a user who already holds one valid refresh token logs in again from a second client
- **THEN** the system issues a second, independent refresh token and the first refresh token remains valid

### Requirement: JWT Access Token Validation
The system SHALL sign access tokens using HS256 with a signing secret sourced from external configuration (never committed to source control). The `sub` claim SHALL carry the authenticated user's id.

Every request to an authenticated endpoint SHALL be rejected unless the presented access token has a valid signature, is unexpired, and (where issuer/audience are configured) matches the configured issuer and audience.

#### Scenario: Valid access token accepted
- **WHEN** a request presents an unexpired, correctly signed access token for an authenticated endpoint
- **THEN** the system processes the request as the user identified by the token's `sub` claim

#### Scenario: Expired access token rejected
- **WHEN** a request presents an access token whose expiration time has passed
- **THEN** the system rejects the request with `401 Unauthorized`

#### Scenario: Tampered or invalidly signed token rejected
- **WHEN** a request presents an access token whose signature does not validate against the configured signing secret
- **THEN** the system rejects the request with `401 Unauthorized`

#### Scenario: Missing credentials rejected
- **WHEN** a request to an authenticated endpoint carries no Authorization header or bearer token
- **THEN** the system rejects the request with `401 Unauthorized`

### Requirement: Current User Lookup
The system SHALL provide an authenticated endpoint that returns the profile of the user identified by the presented access token's `sub` claim. This endpoint exists so JWT access-token validation (above) is exercisable end-to-end, and so a client can confirm who it is currently authenticated as.

#### Scenario: Returns the authenticated user's profile
- **WHEN** an authenticated request presents a valid, unexpired access token
- **THEN** the system responds `200 OK` with that user's id, name, and email

#### Scenario: Rejected without a valid access token
- **WHEN** a request to this endpoint carries no token, an expired token, or a token with an invalid signature
- **THEN** the system rejects the request with `401 Unauthorized`, per the JWT Access Token Validation requirement

### Requirement: Refresh Token Issuance and Storage
The system SHALL generate refresh tokens using a cryptographically secure random generator. The system SHALL persist only a hash of each refresh token; the raw token SHALL be returned to the client exactly once, at issuance, and SHALL NOT be recoverable from storage afterward.

Each refresh token SHALL be associated with exactly one user and SHALL expire 7 days after issuance.

#### Scenario: Refresh token stored as a hash
- **WHEN** a refresh token is issued during login or refresh
- **THEN** the system persists a hash of the token, not the raw token value

### Requirement: Token Refresh with Rotation
The system SHALL allow a client to exchange a valid, unexpired, unrevoked refresh token for a new access token.

Each successful refresh SHALL rotate the refresh token: the system SHALL issue and persist a new refresh token and SHALL revoke the presented one in the same operation, such that the presented token cannot be used again.

#### Scenario: Valid refresh rotates tokens
- **WHEN** a client submits a refresh token that is valid, unexpired, and not revoked
- **THEN** the system responds `200 OK` with a new access token and a new refresh token, and revokes the presented refresh token

#### Scenario: Expired refresh token rejected
- **WHEN** a client submits a refresh token past its 7-day expiration
- **THEN** the system rejects the request with `401 Unauthorized` and does not issue new tokens

#### Scenario: Unknown refresh token rejected
- **WHEN** a client submits a refresh token that does not match any stored hash
- **THEN** the system rejects the request with `401 Unauthorized`

### Requirement: Refresh Token Reuse Detection
Presenting a refresh token that has already been rotated (revoked by a prior refresh) or otherwise explicitly revoked SHALL be treated as evidence of token theft. Detecting this reuse SHALL cause the system to revoke every currently active refresh token belonging to that user, not only the reused one.

#### Scenario: Reused rotated token revokes all sessions
- **WHEN** a client submits a refresh token that was already rotated (and is therefore revoked) by an earlier refresh
- **THEN** the system rejects the request with `401 Unauthorized` and revokes every other active refresh token for that user

#### Scenario: Sessions revoked by reuse detection cannot refresh
- **WHEN** a refresh token was revoked as a side effect of reuse detection on a different token for the same user
- **THEN** a subsequent refresh attempt using that revoked token is rejected with `401 Unauthorized`

### Requirement: Logout
The system SHALL allow an authenticated user to log out by presenting their refresh token. Logout SHALL revoke exactly that refresh token. Logout SHALL NOT revoke the user's other active refresh tokens.

A revoked refresh token SHALL NOT be usable afterward to obtain a new access token.

#### Scenario: Logout revokes the presented session
- **WHEN** an authenticated user calls logout with their current refresh token
- **THEN** the system revokes that refresh token and responds `204 No Content`

#### Scenario: Revoked token cannot be refreshed
- **WHEN** a client attempts to use a refresh token after it has been revoked via logout
- **THEN** the system rejects the request with `401 Unauthorized`

#### Scenario: Logout does not affect other sessions
- **WHEN** a user with two active sessions logs out of one of them
- **THEN** the refresh token belonging to the other, unaffected session remains valid for subsequent refresh requests

### Requirement: Forgot Password
The system SHALL allow an unauthenticated visitor to request a password-reset code by submitting an email address.

The response SHALL be identical (`200 OK`, generic message) whether or not the submitted email belongs to a registered user, so that the endpoint does not reveal account existence.

When the email belongs to a registered user, the system SHALL generate a 6-digit numeric one-time password (OTP), persist only a hash of it, set its expiry to 10 minutes from issuance, and log the raw OTP to the application console/logging system (no real email provider is used).

Issuing a new OTP for a user SHALL invalidate any other OTP previously issued to that user that is still unexpired and unused — at most one OTP is ever valid for a user at a time.

If a request for the same email arrives within 60 seconds of the last OTP actually issued to that email, the system SHALL NOT issue a new OTP (the existing one keeps its original expiry) but SHALL still return the same generic `200 OK` response, so the cooldown itself cannot be used to detect whether the email exists.

#### Scenario: Registered email issues an OTP
- **WHEN** a visitor submits the email address of a registered user
- **THEN** the system generates a 6-digit numeric OTP, persists a hash of it with a 10-minute expiry, logs the raw OTP to the console, and responds `200 OK` with a generic message

#### Scenario: Unknown email gives the same generic response
- **WHEN** a visitor submits an email address with no matching account
- **THEN** the system responds `200 OK` with the same generic message used for a registered email, and does not generate or log any OTP

#### Scenario: New OTP invalidates the previous one
- **WHEN** a user requests a new OTP while a previously issued OTP for that user is still unexpired and unused
- **THEN** the previous OTP becomes invalid and only the newly issued OTP can be used to reset the password

#### Scenario: Repeat request within cooldown does not reissue an OTP
- **WHEN** a request for the same email arrives within 60 seconds of the last OTP actually issued to that email
- **THEN** the system does not generate a new OTP, the existing OTP's expiry is unchanged, and the system still responds `200 OK` with the generic message

### Requirement: Password Reset
The system SHALL allow a visitor to set a new password by submitting their email, the OTP they received, and a new password.

The new password SHALL satisfy the same password policy used at registration (at least 8 characters, containing at least one letter and at least one digit).

An OTP SHALL be accepted only when it: matches the submitted email, is unexpired, has not already been used, and has not been locked out by exceeding its incorrect-attempt limit. Every rejection reason (wrong OTP, expired OTP, already-used OTP, locked-out OTP, or an email with no matching account) SHALL be rejected with the same generic `400 Bad Request` response, without revealing which condition failed.

Each incorrect OTP submitted against an otherwise-valid, unexpired, unused OTP record SHALL increment that OTP's attempt count. On the 5th incorrect attempt, the OTP SHALL become locked (treated as used/invalid) even though it has not expired, and the user SHALL request a new OTP via forgot-password to try again.

A successful reset SHALL: update the user's password hash, mark the used OTP as used (an OTP is single-use and SHALL be invalid after a successful reset), invalidate every other outstanding OTP for that user, and revoke every one of that user's existing refresh tokens (forcing re-login on all sessions/devices).

#### Scenario: Successful password reset
- **WHEN** a visitor submits a registered email, its currently valid unexpired OTP, and a new password meeting the password policy
- **THEN** the system updates the user's password, marks the OTP used, invalidates any other outstanding OTP for that user, revokes all of that user's refresh tokens, and responds `200 OK`

#### Scenario: Incorrect OTP rejected
- **WHEN** a visitor submits a registered email with an OTP that does not match the currently valid one for that user
- **THEN** the system rejects the request with `400 Bad Request` using the same generic message as any other invalid-reset reason, and increments that OTP's incorrect-attempt count

#### Scenario: Expired OTP rejected
- **WHEN** a visitor submits an OTP whose 10-minute expiry has passed
- **THEN** the system rejects the request with `400 Bad Request` and does not update the password

#### Scenario: Already-used OTP rejected
- **WHEN** a visitor submits an OTP that has already been consumed by a prior successful password reset
- **THEN** the system rejects the request with `400 Bad Request` and does not update the password

#### Scenario: OTP locked out after 5 incorrect attempts
- **WHEN** a visitor submits 5 incorrect OTPs in a row against the same otherwise-valid, unexpired OTP record
- **THEN** the system locks that OTP so that even the correct code is no longer accepted, and rejects the request with `400 Bad Request`

#### Scenario: Unknown email rejected with the same generic error
- **WHEN** a visitor submits an email with no matching account
- **THEN** the system rejects the request with `400 Bad Request` using the same generic message used for an incorrect or expired OTP, without revealing that the email does not exist

#### Scenario: Password below policy rejected
- **WHEN** a visitor submits a valid, unexpired OTP but a new password shorter than 8 characters, or missing a letter, or missing a digit
- **THEN** the system rejects the request with `400 Bad Request` and does not update the password or consume the OTP

#### Scenario: Successful reset revokes all sessions
- **WHEN** a user with one or more active refresh tokens successfully resets their password
- **THEN** every one of that user's refresh tokens is revoked and none of them can subsequently be used to obtain a new access token

#### Scenario: Successful reset invalidates other outstanding OTPs
- **WHEN** a user has requested more than one OTP in the past (only the newest was ever valid per the Forgot Password requirement) and successfully resets their password with the currently valid OTP
- **THEN** any other outstanding OTP record for that user is also invalidated and cannot be used afterward
