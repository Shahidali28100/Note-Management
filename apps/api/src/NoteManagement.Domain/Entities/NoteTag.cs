namespace NoteManagement.Domain.Entities;

/// <summary>
/// A note-tag association (AB-1006 / SDS §16). Composite identity is (NoteId, TagId) — this
/// class carries no other state and no independent lifecycle of its own.
/// </summary>
public sealed class NoteTag
{
    public Guid NoteId { get; private set; }
    public Guid TagId { get; private set; }

    /// <summary>EF Core materialization only — invariants are enforced via <see cref="Create"/>.</summary>
    private NoteTag()
    {
    }

    public static NoteTag Create(Guid noteId, Guid tagId) => new() { NoteId = noteId, TagId = tagId };
}
