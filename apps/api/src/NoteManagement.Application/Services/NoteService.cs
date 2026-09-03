using NoteManagement.Application.DTOs.Notes;
using NoteManagement.Application.DTOs.Tags;
using NoteManagement.Application.Exceptions;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Services;

/// <summary>The one Application-layer notes orchestrator (spec: notes capability).</summary>
public sealed class NoteService : INoteService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20; // AB-1004 fixed default view — AB-1005 adds real pagination.
    private const int MaxPageSize = 100; // AB-1005: pageSize > 100 clamps down; pageSize < 1 is rejected upstream by NoteListQueryDto's [Range].
    private const string DefaultSortBy = "updatedAt";
    private const string DefaultSortDirection = "desc";

    private readonly INoteRepository _noteRepository;
    private readonly ITagRepository _tagRepository;
    private readonly IUnitOfWork _unitOfWork;

    public NoteService(INoteRepository noteRepository, ITagRepository tagRepository, IUnitOfWork unitOfWork)
    {
        _noteRepository = noteRepository;
        _tagRepository = tagRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>FRS-NOTE-001. Trims Title/Content before persisting — the spec validates bounds "after trimming," so storage follows the same normalization. AB-1006: tagIds is validated (ownership) before the note is created — no partial creation on an invalid id.</summary>
    public async Task<NoteResponseDto> CreateAsync(Guid userId, CreateNoteRequestDto request, CancellationToken cancellationToken)
    {
        var tagIds = await ResolveTagIdsAsync(userId, request.TagIds, cancellationToken);
        var note = Note.Create(userId, request.Title.Trim(), request.Content.Trim());

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            _noteRepository.Add(note);
            await _noteRepository.ReplaceTagsForNoteAsync(note.Id, tagIds, ct);
            // Single SaveChangesAsync — EF Core orders the Note insert before its NoteTags rows
            // automatically (both are tracked as Added on the same DbContext/transaction).
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return await MapWithTagsAsync(note, cancellationToken);
    }

    /// <summary>FRS-NOTE-002. GetByIdAsync's ownership+soft-delete scoping means "missing," "not yours," and "deleted" are indistinguishable here — all three surface as the same NoteNotFoundException.</summary>
    public async Task<NoteResponseDto> GetByIdAsync(Guid userId, Guid noteId, CancellationToken cancellationToken)
    {
        var note = await _noteRepository.GetByIdAsync(noteId, userId, cancellationToken)
            ?? throw new NoteNotFoundException(noteId);
        return await MapWithTagsAsync(note, cancellationToken);
    }

    /// <summary>
    /// FRS-NOTE-002/006/007. Defaults + the pageSize>100 clamp are resolved here — NoteRepository
    /// receives only fully-valid values. page/pageSize &lt; 1, malformed values, or sortBy/
    /// sortDirection outside the allowlist never reach this method (rejected upstream by
    /// NoteListQueryDto's DataAnnotations, see class remarks). When query is all-null, behavior
    /// is identical to AB-1004's fixed default view. AB-1006: tagId (FRS-NOTE-008) is validated
    /// as owned by the caller before the repository is ever queried — an invalid/unowned tagId
    /// is 400, never a silent empty page (SDS §42).
    /// </summary>
    public async Task<NoteListResponseDto> ListAsync(Guid userId, NoteListQueryDto query, CancellationToken cancellationToken)
    {
        if (query.TagId is Guid tagId)
        {
            var owned = await _tagRepository.GetOwnedIdsAsync(userId, new[] { tagId }, cancellationToken);
            if (!owned.Contains(tagId))
            {
                throw new InvalidTagReferenceException(new[] { tagId });
            }
        }

        var page = query.Page ?? DefaultPage;
        var pageSize = Math.Min(query.PageSize ?? DefaultPageSize, MaxPageSize);
        var sortBy = query.SortBy ?? DefaultSortBy;
        var sortDirection = query.SortDirection ?? DefaultSortDirection;

        var (items, totalCount) = await _noteRepository.GetPageForUserAsync(userId, page, pageSize, sortBy, sortDirection, query.TagId, cancellationToken);
        var tagsByNote = await _noteRepository.GetTagsForNotesAsync(items.Select(n => n.Id).ToList(), cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new NoteListResponseDto(
            items.Select(n => Map(n, tagsByNote.GetValueOrDefault(n.Id, Array.Empty<Tag>()))).ToList(),
            page, pageSize, totalCount, totalPages);
    }

    /// <summary>FRS-NOTE-003. Does not create a NoteVersions snapshot — deferred to AB-1009 (proposal.md). AB-1006: tagIds fully replaces the note's tag assignment (missing/empty clears it).</summary>
    public async Task<NoteResponseDto> UpdateAsync(Guid userId, Guid noteId, UpdateNoteRequestDto request, CancellationToken cancellationToken)
    {
        var note = await _noteRepository.GetByIdAsync(noteId, userId, cancellationToken)
            ?? throw new NoteNotFoundException(noteId);
        var tagIds = await ResolveTagIdsAsync(userId, request.TagIds, cancellationToken);

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            note.UpdateContent(request.Title.Trim(), request.Content.Trim());
            await _noteRepository.ReplaceTagsForNoteAsync(noteId, tagIds, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return await MapWithTagsAsync(note, cancellationToken);
    }

    /// <summary>FRS-NOTE-004. GetByIdAsync (soft-delete-filtered) makes an already-deleted note indistinguishable from missing — satisfies the spec's "delete of an already soft-deleted note → 404" scenario for free.</summary>
    public async Task DeleteAsync(Guid userId, Guid noteId, CancellationToken cancellationToken)
    {
        var note = await _noteRepository.GetByIdAsync(noteId, userId, cancellationToken)
            ?? throw new NoteNotFoundException(noteId);

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            note.SoftDelete();
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    /// <summary>FRS-NOTE-005. Looks up including soft-deleted rows so it can distinguish "not found/not owned" (404) from "found but not currently deleted" (409) — the one case that needs both outcomes from a single lookup.</summary>
    public async Task<NoteResponseDto> RestoreAsync(Guid userId, Guid noteId, CancellationToken cancellationToken)
    {
        var note = await _noteRepository.GetByIdIncludingDeletedAsync(noteId, userId, cancellationToken)
            ?? throw new NoteNotFoundException(noteId);

        if (!note.IsDeleted)
        {
            throw new NoteNotDeletedException(noteId);
        }

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            note.Restore();
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return await MapWithTagsAsync(note, cancellationToken);
    }

    /// <summary>AB-1006. Empty/missing tagIds -&gt; no assignment. Any id GetOwnedIdsAsync doesn't confirm fails the whole request (proposal.md: no partial assignment) — the caller must not proceed to create/update the note.</summary>
    private async Task<IReadOnlyList<Guid>> ResolveTagIdsAsync(Guid userId, IReadOnlyList<Guid>? requested, CancellationToken cancellationToken)
    {
        if (requested is null || requested.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var distinct = requested.Distinct().ToList();
        var owned = await _tagRepository.GetOwnedIdsAsync(userId, distinct, cancellationToken);
        var invalid = distinct.Except(owned).ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidTagReferenceException(invalid);
        }

        return distinct;
    }

    private async Task<NoteResponseDto> MapWithTagsAsync(Note note, CancellationToken cancellationToken)
    {
        var tags = await _noteRepository.GetTagsForNoteAsync(note.Id, cancellationToken);
        return Map(note, tags);
    }

    private static NoteResponseDto Map(Note note, IReadOnlyList<Tag> tags) =>
        new(note.Id, note.Title, note.Content, tags.Select(t => new TagRefDto(t.Id, t.Name, t.Color)).ToList(), note.CreatedAt, note.UpdatedAt);
}
