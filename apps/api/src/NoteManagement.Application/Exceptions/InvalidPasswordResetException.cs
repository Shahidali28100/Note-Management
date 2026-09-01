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
