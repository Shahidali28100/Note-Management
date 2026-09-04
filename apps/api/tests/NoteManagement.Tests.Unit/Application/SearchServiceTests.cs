using NoteManagement.Application.DTOs.Search;
using NoteManagement.Application.Interfaces;
using NoteManagement.Application.Services;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Tests.Unit.Application;

/// <summary>Hand-rolled fakes for both dependencies, matching NoteServiceTests'/TagServiceTests' "no mocking library" convention.</summary>
[TestClass]
public sealed class SearchServiceTests
{
    [TestMethod]
    public async Task SearchAsync_WithDefaultPaging_UsesPage1PageSize20()
    {
        var userId = Guid.NewGuid();
        var note = Note.Create(userId, "Elephant Safari", "Content");
        var searchRepository = new FakeSearchRepository(new[] { note }, totalCount: 1);
        var sut = CreateSut(searchRepository: searchRepository);

        await sut.SearchAsync(userId, new SearchQueryDto("elephant"), CancellationToken.None);

        Assert.AreEqual(1, searchRepository.Calls.Count);
        Assert.AreEqual(1, searchRepository.Calls[0].Page);
        Assert.AreEqual(20, searchRepository.Calls[0].PageSize);
    }

    [TestMethod]
    public async Task SearchAsync_WithExplicitPaging_PassesThroughToRepository()
    {
        var userId = Guid.NewGuid();
        var searchRepository = new FakeSearchRepository(Array.Empty<Note>(), totalCount: 0);
        var sut = CreateSut(searchRepository: searchRepository);

        await sut.SearchAsync(userId, new SearchQueryDto("elephant", Page: 3, PageSize: 5), CancellationToken.None);

        Assert.AreEqual(1, searchRepository.Calls.Count);
        Assert.AreEqual(3, searchRepository.Calls[0].Page);
        Assert.AreEqual(5, searchRepository.Calls[0].PageSize);
    }

    /// <summary>The one clamp rule that's pure Application-layer policy, not expressible via DataAnnotations — must be unit-tested here, same precedent as NoteServiceTests.ListAsync_WithPageSizeOver100_ClampsTo100.</summary>
    [TestMethod]
    public async Task SearchAsync_WithOversizedPageSize_ClampsTo100()
    {
        var userId = Guid.NewGuid();
        var searchRepository = new FakeSearchRepository(Array.Empty<Note>(), totalCount: 0);
        var sut = CreateSut(searchRepository: searchRepository);

        var result = await sut.SearchAsync(userId, new SearchQueryDto("elephant", PageSize: 500), CancellationToken.None);

        Assert.AreEqual(100, result.PageSize);
        Assert.AreEqual(100, searchRepository.Calls[0].PageSize);
    }

    [TestMethod]
    public async Task SearchAsync_WithAllTermsSanitizedAway_ReturnsEmptyPageWithoutCallingRepository()
    {
        var userId = Guid.NewGuid();
        var searchRepository = new FakeSearchRepository(Array.Empty<Note>(), totalCount: 0);
        var sut = CreateSut(searchRepository: searchRepository);

        var result = await sut.SearchAsync(userId, new SearchQueryDto("!!! ???"), CancellationToken.None);

        Assert.AreEqual(0, searchRepository.Calls.Count);
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(0, result.TotalPages);
        Assert.AreEqual(1, result.Page);
        Assert.AreEqual(20, result.PageSize);
    }

    [TestMethod]
    public async Task SearchAsync_MapsResultsWithTagsAndHighlight()
    {
        var userId = Guid.NewGuid();
        var tag = Tag.Create(userId, "Travel", "#FF0000");
        var note = Note.Create(userId, "Elephant Safari", "A trip to see elephants.");
        var searchRepository = new FakeSearchRepository(new[] { note }, totalCount: 1);
        var noteRepository = new FakeNoteRepository(new Dictionary<Guid, IReadOnlyList<Tag>> { [note.Id] = new[] { tag } });
        var sut = CreateSut(searchRepository, noteRepository);

        var result = await sut.SearchAsync(userId, new SearchQueryDto("elephant"), CancellationToken.None);

        Assert.AreEqual(1, result.Items.Count);
        var item = result.Items[0];
        Assert.AreEqual(note.Id, item.Id);
        Assert.AreEqual(1, item.Tags.Count);
        Assert.AreEqual(tag.Id, item.Tags[0].Id);
        Assert.IsTrue(item.Highlight.Title.Contains(SearchHighlighter.SentinelStart));
    }

    private static SearchService CreateSut(FakeSearchRepository? searchRepository = null, FakeNoteRepository? noteRepository = null) =>
        new(searchRepository ?? new FakeSearchRepository(Array.Empty<Note>(), 0), noteRepository ?? new FakeNoteRepository());

    private sealed class FakeSearchRepository : ISearchRepository
    {
        private readonly IReadOnlyList<Note> _items;
        private readonly int _totalCount;

        public FakeSearchRepository(IReadOnlyList<Note> items, int totalCount)
        {
            _items = items;
            _totalCount = totalCount;
        }

        public List<(Guid UserId, IReadOnlyList<string> Terms, int Page, int PageSize)> Calls { get; } = new();

        public Task<(IReadOnlyList<Note> Items, int TotalCount)> SearchAsync(Guid userId, IReadOnlyList<string> terms, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls.Add((userId, terms, page, pageSize));
            return Task.FromResult((_items, _totalCount));
        }
    }

    /// <summary>Only GetTagsForNotesAsync is meaningfully implemented — the only INoteRepository member SearchService actually calls.</summary>
    private sealed class FakeNoteRepository : INoteRepository
    {
        private readonly IReadOnlyDictionary<Guid, IReadOnlyList<Tag>> _tagsByNote;

        public FakeNoteRepository(IReadOnlyDictionary<Guid, IReadOnlyList<Tag>>? tagsByNote = null)
        {
            _tagsByNote = tagsByNote ?? new Dictionary<Guid, IReadOnlyList<Tag>>();
        }

        public void Add(Note note) => throw new NotSupportedException();

        public Task<Note?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Note?> GetByIdIncludingDeletedAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, string sortBy, string sortDirection, Guid? tagId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Tag>> GetTagsForNoteAsync(Guid noteId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Tag>>> GetTagsForNotesAsync(IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken)
        {
            var result = noteIds
                .Where(_tagsByNote.ContainsKey)
                .ToDictionary(id => id, id => _tagsByNote[id]);
            return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Tag>>>(result);
        }

        public Task ReplaceTagsForNoteAsync(Guid noteId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
