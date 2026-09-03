namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Thrown when a tag doesn't exist or isn't owned by the caller — same exception for both so the
/// 404 response never discloses which. Mapped to 404 by ProblemDetailsExceptionHandler.
/// </summary>
public sealed class TagNotFoundException : Exception
{
    public TagNotFoundException(Guid tagId)
        : base($"Tag '{tagId}' was not found.")
    {
    }
}
