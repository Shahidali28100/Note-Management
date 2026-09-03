using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Notes;

/// <summary>
/// FRS-NOTE-003. Same validation as CreateNoteRequestDto — a full replace of title/content.
/// TagIds (AB-1006) also fully replaces the note's tag assignment — missing/empty clears it.
/// </summary>
public sealed record UpdateNoteRequestDto(
    [Required, TrimmedLength(1, 200)] string Title,
    [Required, TrimmedLength(1, int.MaxValue)] string Content,
    IReadOnlyList<Guid>? TagIds = null);
