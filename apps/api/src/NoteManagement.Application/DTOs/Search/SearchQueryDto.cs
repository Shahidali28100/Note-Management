using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Search;

/// <summary>FRS-SEARCH-001/004. Query-string shape for GET /api/search, matching delta-openapi.yaml exactly. The pageSize>100 clamp is a service-layer policy decision, not expressed here (same precedent as NoteListQueryDto).</summary>
public sealed record SearchQueryDto(
    [Required, TrimmedLength(1, 200)] string Q,
    [Range(1, int.MaxValue, ErrorMessage = "page must be a positive integer.")] int? Page = null,
    [Range(1, int.MaxValue, ErrorMessage = "pageSize must be a positive integer.")] int? PageSize = null);
