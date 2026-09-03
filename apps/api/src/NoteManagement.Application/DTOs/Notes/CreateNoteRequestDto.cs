using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Notes;

/// <summary>
/// FRS-NOTE-001. Field shapes match delta-openapi.yaml's CreateNoteRequest exactly.
/// Validation attributes target the constructor parameters, per RegisterRequestDto's
/// established remark — ASP.NET Core's record model-binding validation reads metadata from the
/// primary constructor parameters, not the compiler-generated properties. TagIds (AB-1006) is
/// optional — a missing value means no tags assigned; ownership of each id is validated by
/// NoteService, not here (a shape-only DTO has no repository access).
/// </summary>
public sealed record CreateNoteRequestDto(
    [Required, TrimmedLength(1, 200)] string Title,
    [Required, TrimmedLength(1, int.MaxValue)] string Content,
    IReadOnlyList<Guid>? TagIds = null);
