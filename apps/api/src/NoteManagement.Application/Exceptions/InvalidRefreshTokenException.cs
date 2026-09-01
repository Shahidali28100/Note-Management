namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Thrown by AuthService.RefreshAsync/LogoutAsync when the presented refresh token is unknown,
/// expired, or already revoked. Mapped to 401 by ProblemDetailsExceptionHandler.
/// </summary>
public sealed class InvalidRefreshTokenException : Exception
{
    public InvalidRefreshTokenException()
        : base("Refresh token is invalid, expired, or has already been used.")
    {
    }
}
