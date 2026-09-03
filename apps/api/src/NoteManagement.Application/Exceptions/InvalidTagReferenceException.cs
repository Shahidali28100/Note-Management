namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Thrown when one or more supplied tag ids (Note create/update's tagIds, or the notes list's
/// tagId filter) do not exist or are not owned by the caller. Mapped to 400 — this is input
/// validation, not a resource lookup, so it is never 404 (see plan.md architecture decisions).
/// </summary>
public sealed class InvalidTagReferenceException : Exception
{
    public InvalidTagReferenceException(IReadOnlyCollection<Guid> invalidTagIds)
        : base($"The following tag ids do not exist or are not owned by the caller: {string.Join(", ", invalidTagIds)}.")
    {
    }
}
