namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Thrown by TagService.CreateAsync/UpdateAsync when the (case-insensitive) name collides with
/// another tag owned by the same user. Mapped to 409 by ProblemDetailsExceptionHandler.
/// </summary>
public sealed class DuplicateTagNameException : Exception
{
    public DuplicateTagNameException(string name, Exception? innerException = null)
        : base($"Tag name '{name}' is already in use.", innerException)
    {
    }
}
