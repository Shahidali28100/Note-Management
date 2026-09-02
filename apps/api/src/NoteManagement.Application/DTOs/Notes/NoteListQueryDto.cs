using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Notes;

/// <summary>
/// FRS-NOTE-006/007. Query-string shape for GET /api/notes, matching delta-openapi.yaml's
/// page/pageSize/sortBy/sortDirection parameters exactly. All four are optional — a missing
/// value is NoteService's concern (defaulting), not this DTO's. Range/OptionalAllowedValues
/// attributes only reject genuinely invalid input; the pageSize > 100 clamp is NOT expressed
/// here (that's a service-layer policy decision, not a shape violation) — see
/// NoteService.ListAsync. Uses OptionalAllowedValuesAttribute rather than the built-in
/// AllowedValuesAttribute — the built-in one rejects null (verified directly, not the
/// [Required]-defers-to-null convention the rest of this codebase follows), which would wrongly
/// reject a request that simply omits sortBy/sortDirection.
/// </summary>
public sealed record NoteListQueryDto(
    [Range(1, int.MaxValue, ErrorMessage = "page must be a positive integer.")] int? Page = null,
    [Range(1, int.MaxValue, ErrorMessage = "pageSize must be a positive integer.")] int? PageSize = null,
    [OptionalAllowedValues("createdAt", "updatedAt", "title", ErrorMessage = "sortBy must be one of: createdAt, updatedAt, title.")] string? SortBy = null,
    [OptionalAllowedValues("asc", "desc", ErrorMessage = "sortDirection must be one of: asc, desc.")] string? SortDirection = null);
