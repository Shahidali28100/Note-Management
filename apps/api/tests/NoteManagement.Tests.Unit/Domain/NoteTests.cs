using NoteManagement.Domain.Entities;

namespace NoteManagement.Tests.Unit.Domain;

[TestClass]
public sealed class NoteTests
{
    [TestMethod]
    public void IsDeleted_WhenNotDeleted_ReturnsFalse()
    {
        var note = Note.Create(Guid.NewGuid(), "Title", "Content");

        Assert.IsFalse(note.IsDeleted);
    }

    [TestMethod]
    public void IsDeleted_AfterSoftDelete_ReturnsTrue()
    {
        var note = Note.Create(Guid.NewGuid(), "Title", "Content");

        note.SoftDelete();

        Assert.IsTrue(note.IsDeleted);
    }

    [TestMethod]
    public void UpdateContent_SetsNewValuesAndBumpsUpdatedAt_LeavesCreatedAtUnchanged()
    {
        var note = Note.Create(Guid.NewGuid(), "Old Title", "Old Content");
        var originalCreatedAt = note.CreatedAt;
        var originalUpdatedAt = note.UpdatedAt;

        note.UpdateContent("New Title", "New Content");

        Assert.AreEqual("New Title", note.Title);
        Assert.AreEqual("New Content", note.Content);
        Assert.AreEqual(originalCreatedAt, note.CreatedAt);
        Assert.IsTrue(note.UpdatedAt >= originalUpdatedAt);
    }

    [TestMethod]
    public void SoftDelete_WhenCalledTwice_KeepsFirstDeletedAtTimestamp()
    {
        var note = Note.Create(Guid.NewGuid(), "Title", "Content");

        note.SoftDelete();
        var firstDeletedAt = note.DeletedAt;

        note.SoftDelete();

        Assert.AreEqual(firstDeletedAt, note.DeletedAt);
    }

    [TestMethod]
    public void Restore_ClearsDeletedAt()
    {
        var note = Note.Create(Guid.NewGuid(), "Title", "Content");
        note.SoftDelete();

        note.Restore();

        Assert.IsNull(note.DeletedAt);
        Assert.IsFalse(note.IsDeleted);
    }
}
