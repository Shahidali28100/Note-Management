using NoteManagement.Domain.Entities;

namespace NoteManagement.Tests.Unit.Domain;

[TestClass]
public sealed class NoteTagTests
{
    [TestMethod]
    public void Create_SetsNoteIdAndTagId()
    {
        var noteId = Guid.NewGuid();
        var tagId = Guid.NewGuid();

        var noteTag = NoteTag.Create(noteId, tagId);

        Assert.AreEqual(noteId, noteTag.NoteId);
        Assert.AreEqual(tagId, noteTag.TagId);
    }
}
