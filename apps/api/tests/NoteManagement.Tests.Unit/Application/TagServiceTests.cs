using NoteManagement.Application.DTOs.Tags;
using NoteManagement.Application.Exceptions;
using NoteManagement.Application.Interfaces;
using NoteManagement.Application.Services;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Tests.Unit.Application;

/// <summary>Hand-rolled fakes for both dependencies, matching AuthServiceTests'/NoteServiceTests' "no mocking library" convention.</summary>
[TestClass]
public sealed class TagServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WithValidData_CreatesTag()
    {
        var userId = Guid.NewGuid();
        var tagRepository = new FakeTagRepository();
        var sut = CreateSut(tagRepository: tagRepository);
        var request = new CreateTagRequestDto("Work", "#FF5733");

        var result = await sut.CreateAsync(userId, request, CancellationToken.None);

        Assert.AreEqual(1, tagRepository.Added.Count);
        Assert.AreEqual(userId, tagRepository.Added[0].UserId);
        Assert.AreEqual("Work", result.Name);
        Assert.AreEqual("#FF5733", result.Color);
        Assert.AreEqual(0, result.NoteCount);
        Assert.AreEqual(result.CreatedAt, result.UpdatedAt);
    }

    /// <summary>FRS-TAG-001: relies on the (UserId, Name) unique index (translated to DuplicateTagNameException), same precedent as AuthServiceTests' RegisterAsync_WithDuplicateEmail_ThrowsDuplicateEmailException.</summary>
    [TestMethod]
    public async Task CreateAsync_WithCaseInsensitiveDuplicateName_ThrowsDuplicateTagNameException()
    {
        var unitOfWork = new FakeUnitOfWork { ThrowUniqueConstraintViolationOnNextSave = true };
        var sut = CreateSut(unitOfWork: unitOfWork);
        var request = new CreateTagRequestDto("work", "#00FF00");

        await Assert.ThrowsExactlyAsync<DuplicateTagNameException>(() => sut.CreateAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    [TestMethod]
    public async Task ListAsync_WithNoTags_ReturnsEmptyArray()
    {
        var sut = CreateSut();

        var result = await sut.ListAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task UpdateAsync_WithValidData_UpdatesNameAndColor()
    {
        var userId = Guid.NewGuid();
        var tag = Tag.Create(userId, "Old Name", "#000000");
        var sut = CreateSut(tagRepository: new FakeTagRepository(tag));

        var result = await sut.UpdateAsync(userId, tag.Id, new UpdateTagRequestDto("New Name", "#FFFFFF"), CancellationToken.None);

        Assert.AreEqual("New Name", result.Name);
        Assert.AreEqual("#FFFFFF", result.Color);
    }

    [TestMethod]
    public async Task UpdateAsync_WithDuplicateName_ThrowsDuplicateTagNameException()
    {
        var userId = Guid.NewGuid();
        var tag = Tag.Create(userId, "Old Name", "#000000");
        var unitOfWork = new FakeUnitOfWork { ThrowUniqueConstraintViolationOnNextSave = true };
        var sut = CreateSut(tagRepository: new FakeTagRepository(tag), unitOfWork: unitOfWork);

        await Assert.ThrowsExactlyAsync<DuplicateTagNameException>(
            () => sut.UpdateAsync(userId, tag.Id, new UpdateTagRequestDto("Existing Name", "#FFFFFF"), CancellationToken.None));
    }

    [TestMethod]
    public async Task UpdateAsync_WithUnknownId_ThrowsTagNotFoundException()
    {
        var sut = CreateSut();

        await Assert.ThrowsExactlyAsync<TagNotFoundException>(
            () => sut.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateTagRequestDto("Name", "#FFFFFF"), CancellationToken.None));
    }

    [TestMethod]
    public async Task DeleteAsync_WithOwnedTag_RemovesTag()
    {
        var userId = Guid.NewGuid();
        var tag = Tag.Create(userId, "Name", "#000000");
        var tagRepository = new FakeTagRepository(tag);
        var sut = CreateSut(tagRepository: tagRepository);

        await sut.DeleteAsync(userId, tag.Id, CancellationToken.None);

        Assert.AreEqual(1, tagRepository.Removed.Count);
        Assert.AreEqual(tag.Id, tagRepository.Removed[0].Id);
    }

    [TestMethod]
    public async Task DeleteAsync_WithUnknownId_ThrowsTagNotFoundException()
    {
        var sut = CreateSut();

        await Assert.ThrowsExactlyAsync<TagNotFoundException>(
            () => sut.DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    private static TagService CreateSut(FakeTagRepository? tagRepository = null, FakeUnitOfWork? unitOfWork = null) =>
        new(tagRepository ?? new FakeTagRepository(), unitOfWork ?? new FakeUnitOfWork());

    private sealed class FakeTagRepository : ITagRepository
    {
        private readonly List<Tag> _all;

        public FakeTagRepository(params Tag[] existing)
        {
            _all = existing.ToList();
        }

        public List<Tag> Added { get; } = new();

        public List<Tag> Removed { get; } = new();

        public void Add(Tag tag)
        {
            Added.Add(tag);
            _all.Add(tag);
        }

        public void Remove(Tag tag)
        {
            Removed.Add(tag);
            _all.Remove(tag);
        }

        public Task<Tag?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_all.FirstOrDefault(t => t.Id == id && t.UserId == userId));

        public Task<IReadOnlyList<Tag>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Tag>>(_all.Where(t => t.UserId == userId).ToList());

        public Task<IReadOnlyDictionary<Guid, int>> GetActiveNoteCountsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(_all.Where(t => t.UserId == userId).ToDictionary(t => t.Id, _ => 0));

        public Task<IReadOnlyList<Guid>> GetOwnedIdsAsync(Guid userId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(_all.Where(t => t.UserId == userId && tagIds.Contains(t.Id)).Select(t => t.Id).ToList());
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public bool ThrowUniqueConstraintViolationOnNextSave { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (ThrowUniqueConstraintViolationOnNextSave)
            {
                ThrowUniqueConstraintViolationOnNextSave = false;
                throw new UniqueConstraintViolationException(new InvalidOperationException("simulated unique-index violation"));
            }

            return Task.CompletedTask;
        }

        public Task RunInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
