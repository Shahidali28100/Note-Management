using NoteManagement.Application.DTOs.Notes;
using NoteManagement.Application.Exceptions;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Services;

/// <summary>The one Application-layer notes orchestrator (spec: notes capability).</summary>
public sealed class NoteService : INoteService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20; // AB-1004 fixed default view — AB-1005 adds real pagination.

    private readonly INoteRepository _noteRepository;
    private readonly IUnitOfWork _unitOfWork;

    public NoteService(INoteRepository noteRepository, IUnitOfWork unitOfWork)
    {
        _noteRepository = noteRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>FRS-NOTE-001. Trims Title/Content before persisting — the spec validates bounds "after trimming," so storage follows the same normalization.</summary>
    public async Task<NoteResponseDto> CreateAsync(Guid userId, CreateNoteRequestDto request, CancellationToken cancellationToken)
    {
        var note = Note.Create(userId, request.Title.Trim(), request.Content.Trim());

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            _noteRepository.Add(note);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return Map(note);
    }

    /// <summary>FRS-NOTE-002. GetByIdAsync's ownership+soft-delete scoping means "missing," "not yours," and "deleted" are indistinguishable here — all three surface as the same NoteNotFoundException.</summary>
    public async Task<NoteResponseDto> GetByIdAsync(Guid userId, Guid noteId, CancellationToken cancellationToken)
    {
        var note = await _noteRepository.GetByIdAsync(noteId, userId, cancellationToken)
            ?? throw new NoteNotFoundException(noteId);
        return Map(note);
    }

    /// <summary>FRS-NOTE-002/006/007 (fixed default view only — see class remarks and proposal.md).</summary>
    public async Task<NoteListResponseDto> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _noteRepository.GetPageForUserAsync(userId, DefaultPage, DefaultPageSize, cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)DefaultPageSize);

        return new NoteListResponseDto(items.Select(Map).ToList(), DefaultPage, DefaultPageSize, totalCount, totalPages);
    }

    /// <summary>FRS-NOTE-003. Does not create a NoteVersions snapshot — deferred to AB-1009 (proposal.md).</summary>
    public async Task<NoteResponseDto> UpdateAsync(Guid userId, Guid noteId, UpdateNoteRequestDto request, CancellationToken cancellationToken)
    {
        var note = await _noteRepository.GetByIdAsync(noteId, userId, cancellationToken)
            ?? throw new NoteNotFoundException(noteId);

        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            note.UpdateContent(request.Title.Trim(), request.Content.Trim());
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return Map(note);
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

        return Map(note);
    }

    private static NoteResponseDto Map(Note note) => new(note.Id, note.Title, note.Content, note.CreatedAt, note.UpdatedAt);
}
