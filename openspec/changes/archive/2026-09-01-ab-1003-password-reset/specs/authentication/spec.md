## ADDED Requirements

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
