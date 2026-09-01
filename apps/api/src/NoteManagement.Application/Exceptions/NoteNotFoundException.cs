namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Thrown when a note doesn't exist, isn't owned by the caller, or (for non-restore lookups) is
/// soft-deleted — same exception for all three so the 404 response never distinguishes them
/// (spec: "no existence/ownership disclosure"). Mapped to 404 by ProblemDetailsExceptionHandler.
/// </summary>
public sealed class NoteNotFoundException : Exception
{
    public NoteNotFoundException(Guid noteId)
        : base($"Note '{noteId}' was not found.")
    {
    }
}
