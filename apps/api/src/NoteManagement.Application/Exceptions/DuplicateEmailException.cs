namespace NoteManagement.Application.Exceptions;

/// <summary>Thrown by AuthService.RegisterAsync when the email is already registered. Mapped to 409 by ProblemDetailsExceptionHandler.</summary>
public sealed class DuplicateEmailException : Exception
{
    public DuplicateEmailException(string email, Exception? innerException = null)
        : base($"Email '{email}' is already registered.", innerException)
    {
    }
}
