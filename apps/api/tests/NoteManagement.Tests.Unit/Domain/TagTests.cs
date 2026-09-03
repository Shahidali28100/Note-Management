using NoteManagement.Domain.Entities;

namespace NoteManagement.Tests.Unit.Domain;

[TestClass]
public sealed class TagTests
{
    [TestMethod]
    public void Create_SetsAllFields()
    {
        var userId = Guid.NewGuid();

        var tag = Tag.Create(userId, "Work", "#FF5733");

        Assert.AreNotEqual(Guid.Empty, tag.Id);
        Assert.AreEqual(userId, tag.UserId);
        Assert.AreEqual("Work", tag.Name);
        Assert.AreEqual("#FF5733", tag.Color);
        Assert.AreEqual(tag.CreatedAt, tag.UpdatedAt);
    }

    [TestMethod]
    public void Rename_UpdatesNameColorAndUpdatedAt_LeavesOwnerUnchanged()
    {
        var userId = Guid.NewGuid();
        var tag = Tag.Create(userId, "Old Name", "#000000");
        var originalCreatedAt = tag.CreatedAt;
        var originalUpdatedAt = tag.UpdatedAt;

        tag.Rename("New Name", "#FFFFFF");

        Assert.AreEqual("New Name", tag.Name);
        Assert.AreEqual("#FFFFFF", tag.Color);
        Assert.AreEqual(userId, tag.UserId);
        Assert.AreEqual(originalCreatedAt, tag.CreatedAt);
        Assert.IsTrue(tag.UpdatedAt >= originalUpdatedAt);
    }
}
