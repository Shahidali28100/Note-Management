namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Thrown by RestoreAsync when the note exists and is owned by the caller but isn't currently
/// soft-deleted. Mapped to 409 by ProblemDetailsExceptionHandler.
/// </summary>
public sealed class NoteNotDeletedException : Exception
{
    public NoteNotDeletedException(Guid noteId)
        : base($"Note '{noteId}' is not deleted; nothing to restore.")
    {
    }
}
