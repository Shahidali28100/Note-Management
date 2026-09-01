using NoteManagement.Application.DTOs.Notes;

namespace NoteManagement.Application.Interfaces;

public interface INoteService
{
    Task<NoteResponseDto> CreateAsync(Guid userId, CreateNoteRequestDto request, CancellationToken cancellationToken);

    Task<NoteResponseDto> GetByIdAsync(Guid userId, Guid noteId, CancellationToken cancellationToken);

    Task<NoteListResponseDto> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<NoteResponseDto> UpdateAsync(Guid userId, Guid noteId, UpdateNoteRequestDto request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, Guid noteId, CancellationToken cancellationToken);

    Task<NoteResponseDto> RestoreAsync(Guid userId, Guid noteId, CancellationToken cancellationToken);
}
