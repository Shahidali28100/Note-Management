using NoteManagement.Application.DTOs.Tags;
using NoteManagement.Application.Exceptions;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Services;

/// <summary>The one Application-layer tags orchestrator (spec: tags capability).</summary>
public sealed class TagService : ITagService
{
    private readonly ITagRepository _tagRepository;
    private readonly IUnitOfWork _unitOfWork;

    public TagService(ITagRepository tagRepository, IUnitOfWork unitOfWork)
    {
        _tagRepository = tagRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>FRS-TAG-001. Relies on the Tags (UserId, Name) unique index (translated to DuplicateTagNameException here) rather than a pre-check, to avoid a check-then-insert race — same precedent as AuthService.RegisterAsync.</summary>
    public async Task<TagResponseDto> CreateAsync(Guid userId, CreateTagRequestDto request, CancellationToken cancellationToken)
    {
        var tag = Tag.Create(userId, request.Name.Trim(), request.Color);

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            _tagRepository.Add(tag);
            try
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (UniqueConstraintViolationException ex)
            {
                throw new DuplicateTagNameException(request.Name, ex);
            }
        }, cancellationToken);

        return Map(tag, noteCount: 0);
    }

    /// <summary>FRS-TAG-004.</summary>
    public async Task<IReadOnlyList<TagResponseDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tags = await _tagRepository.GetAllForUserAsync(userId, cancellationToken);
        var counts = await _tagRepository.GetActiveNoteCountsAsync(userId, cancellationToken);
        return tags.Select(t => Map(t, counts.GetValueOrDefault(t.Id))).ToList();
    }

    /// <summary>FRS-TAG-002. "Update to unchanged name allowed" needs no special-case code — a row updated to a value that only collides with its own current value is not a uniqueness violation.</summary>
    public async Task<TagResponseDto> UpdateAsync(Guid userId, Guid tagId, UpdateTagRequestDto request, CancellationToken cancellationToken)
    {
        var tag = await _tagRepository.GetByIdAsync(tagId, userId, cancellationToken)
            ?? throw new TagNotFoundException(tagId);

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            tag.Rename(request.Name.Trim(), request.Color);
            try
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (UniqueConstraintViolationException ex)
            {
                throw new DuplicateTagNameException(request.Name, ex);
            }
        }, cancellationToken);

        var counts = await _tagRepository.GetActiveNoteCountsAsync(userId, cancellationToken);
        return Map(tag, counts.GetValueOrDefault(tag.Id));
    }

    /// <summary>FRS-TAG-003. NoteTagConfiguration's FK cascade removes this tag's NoteTags rows automatically — no manual association cleanup needed here.</summary>
    public async Task DeleteAsync(Guid userId, Guid tagId, CancellationToken cancellationToken)
    {
        var tag = await _tagRepository.GetByIdAsync(tagId, userId, cancellationToken)
            ?? throw new TagNotFoundException(tagId);

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            _tagRepository.Remove(tag);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    private static TagResponseDto Map(Tag tag, int noteCount) =>
        new(tag.Id, tag.Name, tag.Color, noteCount, tag.CreatedAt, tag.UpdatedAt);
}
