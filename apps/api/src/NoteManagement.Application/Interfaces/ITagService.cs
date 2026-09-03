using NoteManagement.Application.DTOs.Tags;

namespace NoteManagement.Application.Interfaces;

public interface ITagService
{
    Task<TagResponseDto> CreateAsync(Guid userId, CreateTagRequestDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<TagResponseDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<TagResponseDto> UpdateAsync(Guid userId, Guid tagId, UpdateTagRequestDto request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, Guid tagId, CancellationToken cancellationToken);
}
