# Technical Plan — AB-1005 Notes Pagination

Source artifacts: `proposal.md`, `specs/notes/spec.md` (MODIFIED "Note Listing"), `delta-openapi.yaml`. No DB schema change. No frontend UI change (AB-1011 consumes this later) — only `packages/shared` contracts are touched, per SDS §55/§81 and `packages/shared/CLAUDE.md`.

## 1. Architecture Decisions

| Decision | Reasoning |
|---|---|
| **Validation lives at the Api boundary via `[ApiController]` automatic 400** (DataAnnotations on a new query DTO), not hand-written controller checks. | Matches the existing `CreateNoteRequestDto`/`UpdateNoteRequestDto` convention exactly (SDS §5.1: controllers stay thin). No new exception type, no change to `ProblemDetailsExceptionHandler` — malformed/out-of-range/non-allowlisted query values never reach the Application layer. |
| **Defaulting (`page`→1, `pageSize`→20, `sortBy`→`updatedAt`, `sortDirection`→`desc`) and the `pageSize > 100` clamp live in `NoteService`, not the DTO or the repository.** | Defaulting/clamping is business policy (SDS §5.2 Application layer: "use cases, business workflows"), not a pure shape check (that's DataAnnotations' job) and not a persistence concern (that's the repository's job). `NoteRepository` receives fully-resolved, already-valid values. |
| **`pageSize < 1` rejects (`400`), only `pageSize > 100` clamps.** | Per user-approved correction to the proposal: a non-positive value is invalid input (same class of error as `page < 1`), not a magnitude to silently forgive. Only "too large" is a clamp-worthy magnitude problem. |
| **Sorting is an explicit `switch` expression in `NoteRepository` mapping `(sortBy, sortDirection)` → a fixed `OrderBy`/`OrderByDescending` column expression — no dynamic/reflection-based column selection (e.g. no `EF.Property<object>(n, sortBy)`).** | AGENTS.md §6 / SDS §41/§59: sorting must use an explicit allowlist, never let a query value reach LINQ/SQL construction dynamically. The switch's `default` arm falls back to `updatedAt desc` rather than throwing — defense in depth, since by the time this code runs `sortBy`/`sortDirection` have already passed the `[AllowedValues]` check, but the repository never trusts that as its only line of defense. |
| **A new `OptionalAllowedValuesAttribute` (hand-rolled, `Application/Validation/`) is used for the `sortBy`/`sortDirection` allowlists — not the built-in `System.ComponentModel.DataAnnotations.AllowedValuesAttribute`.** | Originally planned to reuse the built-in `AllowedValuesAttribute` (available since .NET 8). **Corrected during implementation**: direct testing (`AllowedValuesAttribute.IsValid(null)`) showed it returns `false` — the built-in attribute rejects a missing value outright, which would wrongly reject any `GET /api/notes` request that simply omits `sortBy`/`sortDirection`, breaking the "missing defaults" requirement. `OptionalAllowedValuesAttribute` follows the same "null is `[Required]`'s concern" convention `TrimmedLengthAttribute` already establishes in this codebase — checked directly rather than assumed, this time. |
| **`GetPageForUserAsync`'s existing 4-parameter signature (`userId, page, pageSize`) is extended in place with `sortBy, sortDirection`, not replaced by a new method.** | AB-1004 deliberately shaped this method to be extended by AB-1005 without a breaking change (see its XML doc remark). One list method, one call site (`NoteService.ListAsync`) — no reason to fork. |

## 2. Files to Modify (no new files outside DTOs/tests)

### Domain — no changes
`Note.cs` is untouched; nothing about sorting/paging is a domain invariant.

### Application

**`apps/api/src/NoteManagement.Application/DTOs/Notes/NoteListQueryDto.cs`** (NEW)
```csharp
using System.ComponentModel.DataAnnotations;

namespace NoteManagement.Application.DTOs.Notes;

/// <summary>
/// FRS-NOTE-006/007. Query-string shape for GET /api/notes, matching delta-openapi.yaml's
/// page/pageSize/sortBy/sortDirection parameters exactly. All four are optional — a missing
/// value is NoteService's concern (defaulting), not this DTO's. Range/AllowedValues attributes
/// only reject genuinely invalid input; the pageSize > 100 clamp is NOT expressed here (that's
/// a service-layer policy decision, not a shape violation) — see NoteService.ListAsync.
/// </summary>
public sealed record NoteListQueryDto(
    [Range(1, int.MaxValue, ErrorMessage = "page must be a positive integer.")] int? Page = null,
    [Range(1, int.MaxValue, ErrorMessage = "pageSize must be a positive integer.")] int? PageSize = null,
    [OptionalAllowedValues("createdAt", "updatedAt", "title", ErrorMessage = "sortBy must be one of: createdAt, updatedAt, title.")] string? SortBy = null,
    [OptionalAllowedValues("asc", "desc", ErrorMessage = "sortDirection must be one of: asc, desc.")] string? SortDirection = null);
```

**`apps/api/src/NoteManagement.Application/Validation/OptionalAllowedValuesAttribute.cs`** (NEW — added during implementation; see the architecture-decision table above)
```csharp
public sealed class OptionalAllowedValuesAttribute : ValidationAttribute
{
    private readonly string[] _allowedValues;

    public OptionalAllowedValuesAttribute(params string[] allowedValues)
    {
        _allowedValues = allowedValues;
        ErrorMessage = $"Value must be one of: {string.Join(", ", allowedValues)}.";
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string s && _allowedValues.Contains(s, StringComparer.Ordinal));
}
```

**`apps/api/src/NoteManagement.Application/Interfaces/INoteService.cs`** (MODIFY)
- `Task<NoteListResponseDto> ListAsync(Guid userId, CancellationToken cancellationToken);`
  → `Task<NoteListResponseDto> ListAsync(Guid userId, NoteListQueryDto query, CancellationToken cancellationToken);`

**`apps/api/src/NoteManagement.Application/Interfaces/INoteRepository.cs`** (MODIFY)
- `GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)`
  → `GetPageForUserAsync(Guid userId, int page, int pageSize, string sortBy, string sortDirection, CancellationToken cancellationToken)`
- Update the XML doc remark: it currently says "AB-1004 always calls this with page=1/pageSize=20 ... so AB-1005 can wire real query-string values through without changing this signature" — note AB-1005 is now the ticket doing that wiring, and document the `sortBy`/`sortDirection` allowlist values (`createdAt`/`updatedAt`/`title`, `asc`/`desc`) the implementation must handle.

**`apps/api/src/NoteManagement.Application/Services/NoteService.cs`** (MODIFY)
- Add constants: `private const int MaxPageSize = 100;`, `private const string DefaultSortBy = "updatedAt";`, `private const string DefaultSortDirection = "desc";` (alongside existing `DefaultPage`/`DefaultPageSize`).
- Replace `ListAsync`:
```csharp
/// <summary>FRS-NOTE-002/006/007. Defaults + the pageSize>100 clamp are resolved here — NoteRepository
/// receives only fully-valid values. page/pageSize/sortBy &lt; 1 or outside the allowlist never
/// reach this method (rejected upstream by NoteListQueryDto's DataAnnotations, see class remarks).</summary>
public async Task<NoteListResponseDto> ListAsync(Guid userId, NoteListQueryDto query, CancellationToken cancellationToken)
{
    var page = query.Page ?? DefaultPage;
    var pageSize = Math.Min(query.PageSize ?? DefaultPageSize, MaxPageSize);
    var sortBy = query.SortBy ?? DefaultSortBy;
    var sortDirection = query.SortDirection ?? DefaultSortDirection;

    var (items, totalCount) = await _noteRepository.GetPageForUserAsync(userId, page, pageSize, sortBy, sortDirection, cancellationToken);
    var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    return new NoteListResponseDto(items.Select(Map).ToList(), page, pageSize, totalCount, totalPages);
}
```

### Infrastructure

**`apps/api/src/NoteManagement.Infrastructure/Repositories/NoteRepository.cs`** (MODIFY)
```csharp
public async Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(
    Guid userId, int page, int pageSize, string sortBy, string sortDirection, CancellationToken cancellationToken)
{
    var query = _dbContext.Notes.Where(n => n.UserId == userId);
    var totalCount = await query.CountAsync(cancellationToken);

    // Explicit allowlist mapping (AGENTS.md §6, SDS §41/§59) — sortBy/sortDirection are never
    // used to build a query expression dynamically. Falls back to updatedAt desc for any
    // combination outside the four allowlisted sortBy/two sortDirection values, which
    // NoteListQueryDto's [AllowedValues] should already have rejected before this is reached.
    IOrderedQueryable<Note> ordered = (sortBy, sortDirection) switch
    {
        ("createdAt", "asc") => query.OrderBy(n => n.CreatedAt),
        ("createdAt", "desc") => query.OrderByDescending(n => n.CreatedAt),
        ("title", "asc") => query.OrderBy(n => n.Title),
        ("title", "desc") => query.OrderByDescending(n => n.Title),
        ("updatedAt", "asc") => query.OrderBy(n => n.UpdatedAt),
        _ => query.OrderByDescending(n => n.UpdatedAt),
    };

    var items = await ordered
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(cancellationToken);

    return (items, totalCount);
}
```

### Api

**`apps/api/src/NoteManagement.Api/Controllers/NotesController.cs`** (MODIFY)
```csharp
/// <summary>FRS-NOTE-002/006/007.</summary>
[HttpGet]
[ProducesResponseType(typeof(NoteListResponseDto), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public async Task<ActionResult<NoteListResponseDto>> List([FromQuery] NoteListQueryDto query, CancellationToken cancellationToken)
{
    var result = await _noteService.ListAsync(User.GetUserId(), query, cancellationToken);
    return Ok(result);
}
```
`[FromQuery]` is explicit (not left to `[ApiController]`'s inference) so a complex-type parameter on a `GET` action is unambiguously bound from the query string, not the (nonexistent) body.

### DB / EF Core Migrations
**None.** `Notes.UpdatedAt`/`CreatedAt`/`Title` are existing columns; sorting by any of them is an `ORDER BY` on already-materialized columns, no new index required (proposal.md Impact: acceptable at this data scale, not blocking for this ticket). No migration to add.

### Shared TypeScript contracts (`packages/shared`)

**`packages/shared/src/schemas/notes.ts`** (MODIFY — add, don't touch existing schemas)
```typescript
// Mirrors NoteListQueryDto: page/pageSize positive integers, sortBy/sortDirection allowlisted.
// The pageSize>100 clamp and all defaulting are backend (NoteService) behavior — this schema
// only mirrors the *shape* validation (UX convenience, not authoritative — packages/shared/CLAUDE.md).
export const noteListQuerySchema = z.object({
  page: z.number().int().min(1).optional(),
  pageSize: z.number().int().min(1).optional(),
  sortBy: z.enum(['createdAt', 'updatedAt', 'title']).optional(),
  sortDirection: z.enum(['asc', 'desc']).optional(),
});
export type NoteListQuery = z.infer<typeof noteListQuerySchema>;
```

**`packages/shared/src/index.ts`** (MODIFY)
- Add `noteListQuerySchema` to the existing notes export block (line ~23-28).
- `types/notes.ts`'s `export * from '../schemas/notes'` already re-exports the new `NoteListQuery` type — no change needed there (per its own file header: types are derived from the schema file, never hand-duplicated).

No Vitest test required for this addition per `packages/shared/CLAUDE.md` step 6 ("If it wraps logic... add a test") — a bare `z.object` shape mirror with no custom logic follows the same precedent as `noteListResponseSchema`, which has no dedicated test either.

## 3. Test Plan

### Unit — `apps/api/tests/NoteManagement.Tests.Unit/Application/NoteServiceTests.cs` (MODIFY)
- Update every existing `sut.ListAsync(userId, CancellationToken.None)` call site to `sut.ListAsync(userId, new NoteListQueryDto(), CancellationToken.None)` (2 call sites: `ListAsync_ReturnsOnlyCallersActiveNotesSortedByUpdatedAtDesc`, `ListAsync_WithNoNotes_ReturnsEmptyEnvelope`) — behavior unchanged (all-null query = AB-1004's prior fixed defaults).
- Update `FakeNoteRepository.GetPageForUserAsync` to the new 5-parameter signature; keep its own allowlist `switch` (mirrors the real repository) so sort-order assertions are meaningful.
- New tests:
  - `ListAsync_WithExplicitPageAndPageSize_UsesRequestedValues` — page 2/pageSize 5 flow through to the envelope and to `GetPageForUserAsync`'s arguments.
  - `ListAsync_WithPageSizeOver100_ClampsTo100` — `PageSize: 500` in the query → repository called with `pageSize: 100` and envelope reports `PageSize: 100`. (This is the one clamp rule that's pure Application-layer logic, not exercised by DataAnnotations — must be unit-tested here.)
  - `ListAsync_WithSortByTitleAscending_OrdersByTitleAscending` — `SortBy: "title", SortDirection: "asc"` returns notes title-ascending (using the Fake's mirrored switch).
  - `ListAsync_WithNoSortSpecified_DefaultsToUpdatedAtDescending` — already covered by the updated existing test, but confirms `sortBy`/`sortDirection` default through explicitly.

### Integration — `apps/api/tests/NoteManagement.Tests.Integration/Api/NotesControllerTests.cs` (MODIFY)
Existing `List_*` tests (`List_ReturnsOwnedNotesWithPaginationEnvelope`, `List_ExcludesSoftDeletedNotes`, `List_ExcludesOtherUsersNotes`) call `GET /api/notes` with no query string — unchanged, still pass as-is (defaults preserved).

New tests (spec scenario → test name, one each per SDS §76 / AGENTS.md §10 "one named test per scenario"):
- `List_WithPageAndPageSize_ReturnsRequestedPage` — `?page=2&pageSize=5` against a caller with >5 notes; envelope reports `page: 2, pageSize: 5`.
- `List_WithSortByTitleAscending_ReturnsNotesOrderedByTitle` — `?sortBy=title&sortDirection=asc`.
- `List_WithPageBeyondLastPage_ReturnsEmptyItemsNotError` — `?page=999` → `200`, empty `items`, correct `totalCount`/`totalPages`.
- `List_WithInvalidPage_Returns400` — table-style: `page=0`, `page=-1`, `page=abc`, each asserted `400`.
- `List_WithInvalidPageSize_Returns400` — `pageSize=0`, `pageSize=-1`, `pageSize=abc`, each `400`.
- `List_WithPageSizeOver100_ClampsTo100` — `?pageSize=500` → `200`, `pageSize: 100` in the envelope (not rejected).
- `List_WithUnsupportedSortBy_Returns400` — `?sortBy=deletedAt`.
- `List_WithUnsupportedSortDirection_Returns400` — `?sortDirection=sideways`.

This gives every new spec scenario in `specs/notes/spec.md` exactly one named test (AGENTS.md §10 / FRS §15), split unit vs. integration the same way AB-1004's `Create_With...` validation tests are (integration, since `[ApiController]`'s automatic ModelState → 400 only fires through the real MVC pipeline).

## 4. Checkpoint Commands

Run in order; fix and re-run on the first failure before proceeding (root `CLAUDE.md` Quality Gates).

**Backend** (per `apps/api/CLAUDE.md`):
```bash
dotnet build
dotnet test
```

**Shared package / monorepo-wide** (this ticket touches `packages/shared`, which is pnpm-workspace-managed even though there's no UI change — AGENTS.md §12/§4 gates apply to any TS change):
```bash
pnpm lint --max-warnings 0
pnpm build
pnpm test
```
`pnpm test --coverage` before marking the ticket complete (root `CLAUDE.md` Quality Gates / AGENTS.md §4).

No `dotnet ef migrations add` — this change has no schema delta.

## 5. Explicitly Out of Scope (unchanged from proposal.md)
Tag filtering (FRS-NOTE-008 / AB-1006), search (AB-1007), any change to `POST/GET/PUT/DELETE /api/notes/{id}` or `/restore`, any frontend UI (AB-1011/1012).
