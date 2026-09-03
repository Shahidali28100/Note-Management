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
        Assert.AreEqual(0, result.Tags.Count); // No tagIds submitted — the untagged path must not leak tags (AB-1006).
    }

    [TestMethod]
    public async Task CreateAsync_WithTagIds_AssociatesNoteWithTags()
    {
        var userId = Guid.NewGuid();
        var tag1 = Tag.Create(userId, "Work", "#FF0000");
        var tag2 = Tag.Create(userId, "Personal", "#00FF00");
        var tagRepository = new FakeTagRepository(tag1, tag2);
        var sut = CreateSut(noteRepository: new FakeNoteRepository(tagRepository), tagRepository: tagRepository);
        var request = new CreateNoteRequestDto("Title", "Content", new[] { tag1.Id, tag2.Id });

        var result = await sut.CreateAsync(userId, request, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { tag1.Id, tag2.Id }, result.Tags.Select(t => t.Id).ToArray());
    }

    [TestMethod]
    public async Task CreateAsync_WithDuplicateTagIds_AssignsTagExactlyOnce()
    {
        var userId = Guid.NewGuid();
        var tag = Tag.Create(userId, "Work", "#FF0000");
        var tagRepository = new FakeTagRepository(tag);
        var sut = CreateSut(noteRepository: new FakeNoteRepository(tagRepository), tagRepository: tagRepository);
        var request = new CreateNoteRequestDto("Title", "Content", new[] { tag.Id, tag.Id });

        var result = await sut.CreateAsync(userId, request, CancellationToken.None);

        Assert.AreEqual(1, result.Tags.Count);
    }

    [TestMethod]
    public async Task CreateAsync_WithInvalidTagId_ThrowsInvalidTagReferenceException()
    {
        var userId = Guid.NewGuid();
        var sut = CreateSut();
        var request = new CreateNoteRequestDto("Title", "Content", new[] { Guid.NewGuid() });

        await Assert.ThrowsExactlyAsync<InvalidTagReferenceException>(() => sut.CreateAsync(userId, request, CancellationToken.None));
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
        Assert.AreEqual(0, result.Tags.Count); // No tags assigned — the untagged path must not leak tags (AB-1006).
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

        // All-null query — AB-1005's default-resolution must reproduce AB-1004's fixed default
        // view (page 1, pageSize 20, updatedAt desc) exactly.
        var result = await sut.ListAsync(userId, new NoteListQueryDto(), CancellationToken.None);

        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual("Newer", result.Items[0].Title);
        Assert.AreEqual("Older", result.Items[1].Title);
        Assert.AreEqual(2, result.TotalCount);
        Assert.IsTrue(result.Items.All(i => i.Tags.Count == 0)); // No tags assigned — the untagged path must not leak tags (AB-1006).
    }

    [TestMethod]
    public async Task ListAsync_WithNoNotes_ReturnsEmptyEnvelope()
    {
        var sut = CreateSut();

        var result = await sut.ListAsync(Guid.NewGuid(), new NoteListQueryDto(), CancellationToken.None);

        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(0, result.TotalPages);
        Assert.AreEqual(1, result.Page);
        Assert.AreEqual(20, result.PageSize);
    }

    [TestMethod]
    public async Task ListAsync_WithExplicitPageAndPageSize_UsesRequestedValues()
    {
        var userId = Guid.NewGuid();
        var notes = Enumerable.Range(1, 7).Select(i => Note.Create(userId, $"Note {i}", "Content")).ToArray();
        var sut = CreateSut(noteRepository: new FakeNoteRepository(notes));

        var result = await sut.ListAsync(userId, new NoteListQueryDto(Page: 2, PageSize: 3), CancellationToken.None);

        Assert.AreEqual(3, result.Items.Count);
        Assert.AreEqual(2, result.Page);
        Assert.AreEqual(3, result.PageSize);
        Assert.AreEqual(7, result.TotalCount);
        Assert.AreEqual(3, result.TotalPages);
    }

    /// <summary>The one clamp rule that's pure Application-layer policy, not expressible via DataAnnotations — must be unit-tested here (plan.md §3).</summary>
    [TestMethod]
    public async Task ListAsync_WithPageSizeOver100_ClampsTo100()
    {
        var userId = Guid.NewGuid();
        var sut = CreateSut(noteRepository: new FakeNoteRepository(Note.Create(userId, "Title", "Content")));

        var result = await sut.ListAsync(userId, new NoteListQueryDto(PageSize: 500), CancellationToken.None);

        Assert.AreEqual(100, result.PageSize);
    }

    [TestMethod]
    public async Task ListAsync_WithSortByTitleAscending_OrdersByTitleAscending()
    {
        var userId = Guid.NewGuid();
        var bravo = Note.Create(userId, "Bravo", "Content");
        var alpha = Note.Create(userId, "Alpha", "Content");
        var charlie = Note.Create(userId, "Charlie", "Content");
        var sut = CreateSut(noteRepository: new FakeNoteRepository(bravo, alpha, charlie));

        var result = await sut.ListAsync(userId, new NoteListQueryDto(SortBy: "title", SortDirection: "asc"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Alpha", "Bravo", "Charlie" }, result.Items.Select(i => i.Title).ToArray());
    }

    [TestMethod]
    public async Task ListAsync_WithTagIdFilter_ReturnsOnlyNotesCarryingThatTag()
    {
        var userId = Guid.NewGuid();
        var tag = Tag.Create(userId, "Work", "#FF0000");
        var tagRepository = new FakeTagRepository(tag);
        var tagged = Note.Create(userId, "Tagged", "Content");
        var untagged = Note.Create(userId, "Untagged", "Content");
        var noteRepository = new FakeNoteRepository(tagRepository, tagged, untagged);
        await noteRepository.ReplaceTagsForNoteAsync(tagged.Id, new[] { tag.Id }, CancellationToken.None);
        var sut = CreateSut(noteRepository: noteRepository, tagRepository: tagRepository);

        var result = await sut.ListAsync(userId, new NoteListQueryDto(TagId: tag.Id), CancellationToken.None);

        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(tagged.Id, result.Items[0].Id);
    }

    [TestMethod]
    public async Task ListAsync_WithInvalidTagId_ThrowsInvalidTagReferenceException()
    {
        var userId = Guid.NewGuid();
        var sut = CreateSut();

        await Assert.ThrowsExactlyAsync<InvalidTagReferenceException>(
            () => sut.ListAsync(userId, new NoteListQueryDto(TagId: Guid.NewGuid()), CancellationToken.None));
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
        Assert.AreEqual(0, result.Tags.Count); // No tagIds submitted — the untagged path must not leak tags (AB-1006).
    }

    [TestMethod]
    public async Task UpdateAsync_WithTagIds_ReplacesTagAssignment()
    {
        var userId = Guid.NewGuid();
        var tag1 = Tag.Create(userId, "Work", "#FF0000");
        var tag2 = Tag.Create(userId, "Personal", "#00FF00");
        var tagRepository = new FakeTagRepository(tag1, tag2);
        var note = Note.Create(userId, "Title", "Content");
        var noteRepository = new FakeNoteRepository(tagRepository, note);
        await noteRepository.ReplaceTagsForNoteAsync(note.Id, new[] { tag1.Id }, CancellationToken.None);
        var sut = CreateSut(noteRepository: noteRepository, tagRepository: tagRepository);

        var result = await sut.UpdateAsync(userId, note.Id, new UpdateNoteRequestDto("Title", "Content", new[] { tag2.Id }), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { tag2.Id }, result.Tags.Select(t => t.Id).ToArray());
    }

    [TestMethod]
    public async Task UpdateAsync_OmittingPreviouslyAssignedTag_RemovesAssociation()
    {
        var userId = Guid.NewGuid();
        var tag1 = Tag.Create(userId, "Work", "#FF0000");
        var tag2 = Tag.Create(userId, "Personal", "#00FF00");
        var tagRepository = new FakeTagRepository(tag1, tag2);
        var note = Note.Create(userId, "Title", "Content");
        var noteRepository = new FakeNoteRepository(tagRepository, note);
        await noteRepository.ReplaceTagsForNoteAsync(note.Id, new[] { tag1.Id, tag2.Id }, CancellationToken.None);
        var sut = CreateSut(noteRepository: noteRepository, tagRepository: tagRepository);

        var result = await sut.UpdateAsync(userId, note.Id, new UpdateNoteRequestDto("Title", "Content", new[] { tag1.Id }), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { tag1.Id }, result.Tags.Select(t => t.Id).ToArray());
    }

    [TestMethod]
    public async Task UpdateAsync_WithEmptyTagIds_ClearsAllAssignments()
    {
        var userId = Guid.NewGuid();
        var tag = Tag.Create(userId, "Work", "#FF0000");
        var tagRepository = new FakeTagRepository(tag);
        var note = Note.Create(userId, "Title", "Content");
        var noteRepository = new FakeNoteRepository(tagRepository, note);
        await noteRepository.ReplaceTagsForNoteAsync(note.Id, new[] { tag.Id }, CancellationToken.None);
        var sut = CreateSut(noteRepository: noteRepository, tagRepository: tagRepository);

        var result = await sut.UpdateAsync(userId, note.Id, new UpdateNoteRequestDto("Title", "Content", Array.Empty<Guid>()), CancellationToken.None);

        Assert.AreEqual(0, result.Tags.Count);
    }

    [TestMethod]
    public async Task UpdateAsync_WithInvalidTagId_ThrowsInvalidTagReferenceException()
    {
        var userId = Guid.NewGuid();
        var note = Note.Create(userId, "Title", "Content");
        var sut = CreateSut(noteRepository: new FakeNoteRepository(note));
        var request = new UpdateNoteRequestDto("Title", "Content", new[] { Guid.NewGuid() });

        await Assert.ThrowsExactlyAsync<InvalidTagReferenceException>(() => sut.UpdateAsync(userId, note.Id, request, CancellationToken.None));
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
        Assert.AreEqual(0, result.Tags.Count); // No tags assigned — the untagged path must not leak tags (AB-1006).
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

    private static NoteService CreateSut(FakeNoteRepository? noteRepository = null, FakeTagRepository? tagRepository = null, FakeUnitOfWork? unitOfWork = null) =>
        new(noteRepository ?? new FakeNoteRepository(), tagRepository ?? new FakeTagRepository(), unitOfWork ?? new FakeUnitOfWork());

    private sealed class FakeNoteRepository : INoteRepository
    {
        private readonly List<Note> _all;
        private readonly FakeTagRepository _tagRepository;
        private readonly Dictionary<Guid, List<Guid>> _tagIdsByNote = new();

        public FakeNoteRepository(params Note[] existing)
            : this(new FakeTagRepository(), existing)
        {
        }

        /// <summary>AB-1006: pass the same FakeTagRepository instance given to CreateSut so GetTagsForNoteAsync/GetTagsForNotesAsync resolve tag ids against the tags a test actually set up.</summary>
        public FakeNoteRepository(FakeTagRepository tagRepository, params Note[] existing)
        {
            _tagRepository = tagRepository;
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

        public Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, string sortBy, string sortDirection, Guid? tagId, CancellationToken cancellationToken)
        {
            var candidates = _all.Where(n => n.UserId == userId && !n.IsDeleted);

            if (tagId is Guid t)
            {
                candidates = candidates.Where(n => _tagIdsByNote.TryGetValue(n.Id, out var tagIds) && tagIds.Contains(t));
            }

            var totalCount = candidates.Count();

            // Mirrors NoteRepository's explicit allowlist switch so sort-order assertions here
            // are meaningful, not just a pass-through count check.
            IOrderedEnumerable<Note> active = (sortBy, sortDirection) switch
            {
                ("createdAt", "asc") => candidates.OrderBy(n => n.CreatedAt),
                ("createdAt", "desc") => candidates.OrderByDescending(n => n.CreatedAt),
                ("title", "asc") => candidates.OrderBy(n => n.Title, StringComparer.Ordinal),
                ("title", "desc") => candidates.OrderByDescending(n => n.Title, StringComparer.Ordinal),
                ("updatedAt", "asc") => candidates.OrderBy(n => n.UpdatedAt),
                _ => candidates.OrderByDescending(n => n.UpdatedAt),
            };

            var items = active.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<Note>, int)>((items, totalCount));
        }

        public Task<IReadOnlyList<Tag>> GetTagsForNoteAsync(Guid noteId, CancellationToken cancellationToken)
        {
            var tags = ResolveTags(noteId);
            return Task.FromResult<IReadOnlyList<Tag>>(tags);
        }

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Tag>>> GetTagsForNotesAsync(IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken)
        {
            var result = new Dictionary<Guid, IReadOnlyList<Tag>>();
            foreach (var noteId in noteIds)
            {
                var tags = ResolveTags(noteId);
                if (tags.Count > 0)
                {
                    result[noteId] = tags;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Tag>>>(result);
        }

        public Task ReplaceTagsForNoteAsync(Guid noteId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
        {
            _tagIdsByNote[noteId] = tagIds.ToList();
            return Task.CompletedTask;
        }

        private List<Tag> ResolveTags(Guid noteId) =>
            _tagIdsByNote.TryGetValue(noteId, out var tagIds)
                ? tagIds.Select(_tagRepository.Find).Where(t => t is not null).Select(t => t!).ToList()
                : new List<Tag>();
    }

    /// <summary>AB-1006. Hand-rolled fake, same "no mocking library" convention as FakeNoteRepository/FakeUnitOfWork. Shared shape with TagServiceTests' own copy of this fake.</summary>
    private sealed class FakeTagRepository : ITagRepository
    {
        private readonly List<Tag> _all;

        public FakeTagRepository(params Tag[] existing)
        {
            _all = existing.ToList();
        }

        public void Add(Tag tag) => _all.Add(tag);

        public void Remove(Tag tag) => _all.Remove(tag);

        public Task<Tag?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_all.FirstOrDefault(t => t.Id == id && t.UserId == userId));

        public Task<IReadOnlyList<Tag>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Tag>>(_all.Where(t => t.UserId == userId).ToList());

        public Task<IReadOnlyDictionary<Guid, int>> GetActiveNoteCountsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(_all.Where(t => t.UserId == userId).ToDictionary(t => t.Id, _ => 0));

        public Task<IReadOnlyList<Guid>> GetOwnedIdsAsync(Guid userId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(_all.Where(t => t.UserId == userId && tagIds.Contains(t.Id)).Select(t => t.Id).ToList());

        /// <summary>Test-only helper (not part of ITagRepository) letting FakeNoteRepository resolve a tag id to its full Tag for GetTagsForNoteAsync/GetTagsForNotesAsync.</summary>
        public Tag? Find(Guid id) => _all.FirstOrDefault(t => t.Id == id);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RunInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
