namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Thrown by AuthService.LoginAsync for both "unknown email" and "wrong password" — deliberately
/// generic (spec: "does not reveal whether the password or the email was the invalid part").
/// Mapped to 401 by ProblemDetailsExceptionHandler.
/// </summary>
public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("Incorrect email or password.")
    {
    }
}
