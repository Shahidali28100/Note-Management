using NoteManagement.Application.DTOs.Notes;
using NoteManagement.Application.Exceptions;
using NoteManagement.Application.Interfaces;
using NoteManagement.Application.Services;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Tests.Unit.Application;

/// <summary>
/// Hand-rolled fakes for both dependencies, matching AuthServiceTests'/HealthCheckServiceTests'
/// "no mocking library" convention.
/// </summary>
[TestClass]
public sealed class NoteServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WithValidData_CreatesNote()
    {
        var userId = Guid.NewGuid();
        var noteRepository = new FakeNoteRepository();
        var sut = CreateSut(noteRepository: noteRepository);
        var request = new CreateNoteRequestDto("  My Title  ", "  My Content  ");

        var result = await sut.CreateAsync(userId, request, CancellationToken.None);

        Assert.AreEqual(1, noteRepository.Added.Count);
        Assert.AreEqual(userId, noteRepository.Added[0].UserId);
        // Trimmed before persisting — the spec validates bounds "after trimming," so storage follows the same normalization.
        Assert.AreEqual("My Title", result.Title);
        Assert.AreEqual("My Content", result.Content);
        Assert.AreEqual(result.CreatedAt, result.UpdatedAt);
    }

    [TestMethod]
    public async Task GetByIdAsync_WithOwnedNote_ReturnsNote()
    {
        var userId = Guid.NewGuid();
        var note = Note.Create(userId, "Title", "Content");
        var sut = CreateSut(noteRepository: new FakeNoteRepository(note));

        var result = await sut.GetByIdAsync(userId, note.Id, CancellationToken.None);

        Assert.AreEqual(note.Id, result.Id);
        Assert.AreEqual("Title", result.Title);
    }

    [TestMethod]
    public async Task GetByIdAsync_WithUnknownId_ThrowsNoteNotFoundException()
    {
        var sut = CreateSut();

        await Assert.ThrowsExactlyAsync<NoteNotFoundException>(
            () => sut.GetByIdAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task ListAsync_ReturnsOnlyCallersActiveNotesSortedByUpdatedAtDesc()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var older = Note.Create(userId, "Older", "Content");
        Thread.Sleep(15); // Ensure a genuinely later UpdatedAt below, not a tied timestamp.
        var newer = Note.Create(userId, "Newer", "Content");
        newer.UpdateContent("Newer", "Content"); // Bumps UpdatedAt strictly after `older`'s.

        var deletedForCaller = Note.Create(userId, "Deleted", "Content");
        deletedForCaller.SoftDelete();

        var otherUsersNote = Note.Create(otherUserId, "Not mine", "Content");

        var noteRepository = new FakeNoteRepository(older, newer, deletedForCaller, otherUsersNote);
        var sut = CreateSut(noteRepository: noteRepository);

        var result = await sut.ListAsync(userId, CancellationToken.None);

        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual("Newer", result.Items[0].Title);
        Assert.AreEqual("Older", result.Items[1].Title);
        Assert.AreEqual(2, result.TotalCount);
    }

    [TestMethod]
    public async Task ListAsync_WithNoNotes_ReturnsEmptyEnvelope()
    {
        var sut = CreateSut();

        var result = await sut.ListAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(0, result.TotalPages);
        Assert.AreEqual(1, result.Page);
        Assert.AreEqual(20, result.PageSize);
    }

    [TestMethod]
    public async Task UpdateAsync_WithValidData_UpdatesTitleContentAndUpdatedAt()
    {
        var userId = Guid.NewGuid();
        var note = Note.Create(userId, "Old Title", "Old Content");
        var originalCreatedAt = note.CreatedAt;
        var sut = CreateSut(noteRepository: new FakeNoteRepository(note));
        var request = new UpdateNoteRequestDto("  New Title  ", "  New Content  ");

        var result = await sut.UpdateAsync(userId, note.Id, request, CancellationToken.None);

        Assert.AreEqual("New Title", result.Title);
        Assert.AreEqual("New Content", result.Content);
        Assert.AreEqual(originalCreatedAt, result.CreatedAt);
        Assert.IsTrue(result.UpdatedAt >= originalCreatedAt);
    }

    [TestMethod]
    public async Task UpdateAsync_WithUnknownId_ThrowsNoteNotFoundException()
    {
        var sut = CreateSut();
        var request = new UpdateNoteRequestDto("Title", "Content");

        await Assert.ThrowsExactlyAsync<NoteNotFoundException>(
            () => sut.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), request, CancellationToken.None));
    }

    [TestMethod]
    public async Task DeleteAsync_WithOwnedNote_SetsDeletedAt()
    {
        var userId = Guid.NewGuid();
        var note = Note.Create(userId, "Title", "Content");
        var sut = CreateSut(noteRepository: new FakeNoteRepository(note));

        await sut.DeleteAsync(userId, note.Id, CancellationToken.None);

        Assert.IsTrue(note.IsDeleted);
        Assert.IsNotNull(note.DeletedAt);
    }

    [TestMethod]
    public async Task DeleteAsync_WithUnknownId_ThrowsNoteNotFoundException()
    {
        var sut = CreateSut();

        await Assert.ThrowsExactlyAsync<NoteNotFoundException>(
            () => sut.DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task RestoreAsync_WithDeletedNote_ClearsDeletedAt()
    {
        var userId = Guid.NewGuid();
        var note = Note.Create(userId, "Title", "Content");
        note.SoftDelete();
        var sut = CreateSut(noteRepository: new FakeNoteRepository(note));

        var result = await sut.RestoreAsync(userId, note.Id, CancellationToken.None);

        Assert.IsFalse(note.IsDeleted);
        Assert.AreEqual(note.Id, result.Id);
    }

    [TestMethod]
    public async Task RestoreAsync_WithUnknownId_ThrowsNoteNotFoundException()
    {
        var sut = CreateSut();

        await Assert.ThrowsExactlyAsync<NoteNotFoundException>(
            () => sut.RestoreAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task RestoreAsync_WithActiveNote_ThrowsNoteNotDeletedException()
    {
        var userId = Guid.NewGuid();
        var note = Note.Create(userId, "Title", "Content");
        var sut = CreateSut(noteRepository: new FakeNoteRepository(note));

        await Assert.ThrowsExactlyAsync<NoteNotDeletedException>(
            () => sut.RestoreAsync(userId, note.Id, CancellationToken.None));

        Assert.IsFalse(note.IsDeleted);
    }

    [TestMethod]
    public async Task RestoreAsync_LongAfterDeletion_StillSucceeds()
    {
        var userId = Guid.NewGuid();
        var note = Note.Create(userId, "Title", "Content");
        note.SoftDelete();
        // No public API backdates DeletedAt — this test exists specifically to prove no
        // elapsed-time gate exists anywhere in the restore path, so reflection is used to
        // simulate a note deleted well past any hypothetical retention window.
        typeof(Note).GetProperty(nameof(Note.DeletedAt))!.SetValue(note, DateTime.UtcNow.AddDays(-90));
        var sut = CreateSut(noteRepository: new FakeNoteRepository(note));

        var result = await sut.RestoreAsync(userId, note.Id, CancellationToken.None);

        Assert.IsFalse(note.IsDeleted);
        Assert.AreEqual(note.Id, result.Id);
    }

    private static NoteService CreateSut(FakeNoteRepository? noteRepository = null, FakeUnitOfWork? unitOfWork = null) =>
        new(noteRepository ?? new FakeNoteRepository(), unitOfWork ?? new FakeUnitOfWork());

    private sealed class FakeNoteRepository : INoteRepository
    {
        private readonly List<Note> _all;

        public FakeNoteRepository(params Note[] existing)
        {
            _all = existing.ToList();
        }

        public List<Note> Added { get; } = new();

        public void Add(Note note)
        {
            Added.Add(note);
            _all.Add(note);
        }

        public Task<Note?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_all.FirstOrDefault(n => n.Id == id && n.UserId == userId && !n.IsDeleted));

        public Task<Note?> GetByIdIncludingDeletedAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_all.FirstOrDefault(n => n.Id == id && n.UserId == userId));

        public Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var active = _all
                .Where(n => n.UserId == userId && !n.IsDeleted)
                .OrderByDescending(n => n.UpdatedAt)
                .ToList();
            var totalCount = active.Count;
            var items = active.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<Note>, int)>((items, totalCount));
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RunInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
