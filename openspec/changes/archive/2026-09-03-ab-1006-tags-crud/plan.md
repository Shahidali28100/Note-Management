# Technical Plan — AB-1006 Tags CRUD

Source artifacts: `proposal.md`, `specs/tags/spec.md` (ADDED), `specs/notes/spec.md` (MODIFIED "Note Creation"/"Note Update"/"Note Listing"), `delta-openapi.yaml`. New `Tags`/`NoteTags` tables + one EF Core migration. `packages/shared` gets a new `tags` module and additive changes to the existing `notes` module. No `apps/web` change (no frontend tag UI exists until AB-1011/AB-1012).

## 0. Deviation from Approved Plan (recorded post-implementation)

**This plan, as originally approved, specified `Cascade`/`Cascade` for both of `NoteTags`'s foreign keys** (§1's `NoteTagConfiguration.cs` listing and its architecture-decision row). **What actually shipped is `Note → NoteTags` = `Restrict`, `Tag → NoteTags` = `Cascade`.** The rest of this document has already been edited in place to describe the shipped (`Restrict`) design — including the code listing and decision-table row below — so a reader comparing this file against the code will not see a difference. This section exists specifically so the deviation from what was *originally approved* is not lost.

- **What was approved:** both FKs `Cascade`, so deleting either a `Note` row or a `Tag` row would automatically remove the corresponding `NoteTags` rows.
- **What shipped, and why:** during `/implement` Phase 3 (task 3.5), `dotnet ef migrations add AddTagsAndNoteTags` succeeded (it only generates code from the EF model — it never touches a database, so it cannot detect a SQL-Server-specific DDL rule violation). Running `dotnet ef database update` against the real LocalDB instance then failed applying the `Cascade`/`Cascade` design:
  ```
  Introducing FOREIGN KEY constraint 'FK_NoteTags_Tags_TagId' on table 'NoteTags' may cause
  cycles or multiple cascade paths. Specify ON DELETE NO ACTION or ON UPDATE NO ACTION, or
  modify other FOREIGN KEY constraints.
  Error Number:1785
  ```
  SQL Server disallows this because `Users → Notes → NoteTags` and `Users → Tags → NoteTags` both cascade-delete `NoteTags` from the same ancestor table (`Users`), which it cannot resolve unambiguously. This is a genuine SQL Server engine constraint, not an EF Core modeling mistake — the only way to keep both cascades is to break the shared ancestor, which isn't an option here. The fix: `Note → NoteTags` was changed to `Restrict`; `Tag → NoteTags` stays `Cascade` (FRS-TAG-003 requires it, and it's the only one actually exercised by this ticket's code — `TagService.DeleteAsync`).
  - **This was caught only by actually running `dotnet ef database update`, not by `/plan` review, `/tasks` review, `dotnet build`, or any unit/integration test** — none of those execute real DDL against SQL Server. It is not something static review of the plan could reasonably have caught.
- **Consequence for future tickets — read this before adding a hard-delete/purge of `Note` rows:** no ticket currently deletes a `Note` row outright (soft delete is an `UPDATE`, not a `DELETE`). **If a future ticket adds one** (e.g. a 30-day-retention purge job), it **cannot rely on FK cascade** to clean up that note's `NoteTags` rows — the FK is `Restrict`, and the delete will fail with a FK-violation error unless the purge process explicitly deletes the note's `NoteTags` rows first, in the same transaction, before deleting the `Notes` row.
- **Where else this is recorded:** `NoteTagConfiguration.cs`'s own code comment (§2 below); `tasks.md` task 3.5's verification note; the `/implement` summary's Follow-up Tasks. This section is the canonical, explicit statement of the deviation itself — the others describe the fix in the context of the file/task they touch.

## 1. Architecture Decisions

| Decision | Reasoning |
|---|---|
| **`NoteTags` is a genuine, minimal domain entity (`NoteTag`) with a composite key `(NoteId, TagId)` — not an EF Core skip-navigation (`HasMany().WithMany()`) and not navigation collections on `Note`/`Tag`.** | Every existing relationship in this codebase (`RefreshToken`→`User`, `PasswordResetOtp`→`User`, `Note`→`User`) is a one-directional FK-only association configured via `HasOne<X>().WithMany()` with **no navigation property on either side** — domain entities stay flat records with scalar/FK properties only. EF Core's skip-navigation many-to-many *requires* a collection navigation property on both `Note` and `Tag`, which would break that established convention. An explicit, minimal `NoteTag` join entity (`NoteId`, `TagId`, no other columns — matching SDS §16 exactly) preserves it and gives repositories a concrete `DbSet<NoteTag>` to query/mutate directly, the same way they already query `_dbContext.Notes`/`_dbContext.Users` directly. |
| **No separate `INoteTagRepository`.** `NoteRepository` owns tag-assignment reads/writes for a note (`GetTagsForNoteAsync`, `GetTagsForNotesAsync`, `ReplaceTagsForNoteAsync`); `TagRepository` owns the note-count aggregation (`GetActiveNoteCountsAsync`) and tag-ownership validation (`GetOwnedIdsAsync`). Both query `_dbContext.NoteTags` directly. | Matches this codebase's existing pattern of one repository per aggregate root (`Note`, `Tag`) rather than introducing a repository for a pure join table. `NoteTags` has no independent lifecycle or business rules of its own — it's read/written only in service of the `notes` or `tags` capability that's using it, so it doesn't need its own interface. |
| **Case-insensitive per-user tag-name uniqueness (FRS-TAG-001) is enforced by a plain `HasIndex(t => new { t.UserId, t.Name }).IsUnique()` relying on the database's default (case-insensitive) collation — no `COLLATE` clause, no normalized shadow column.** | `UserConfiguration`'s `Users.Email` unique index already relies on exactly this (SQL Server/LocalDB's default `SQL_Latin1_General_CP1_CI_AS` collation is case-insensitive) with no special handling, and login/registration already depend on that behavior. Reusing the identical, already-proven idiom keeps `TagConfiguration` consistent with `UserConfiguration` and avoids introducing a second uniqueness mechanism into the codebase for no behavioral gain. This also means "update to unchanged name allowed" and "excluding the tag being updated" (spec: Tag Update) require **no special-case code at all** — a row updated to a value that collides only with its own current value is not a uniqueness violation from the database's point of view. |
| **Duplicate tag name is detected by catching `UniqueConstraintViolationException` from `SaveChangesAsync` and rethrowing as `DuplicateTagNameException` — no pre-check `ExistsByName` query before insert/update.** | Identical to `AuthService.RegisterAsync`'s established `DuplicateEmailException` pattern (its own doc comment: "relies on the ... unique index ... rather than a pre-check, to avoid a check-then-insert race"). Reusing the same translation path (`IUnitOfWork.SaveChangesAsync` → `UniqueConstraintViolationException`) needs no new plumbing in `UnitOfWork`. |
| **An invalid/unowned tag id is `400 Bad Request` via a new `InvalidTagReferenceException`, never `404`/`403` — for both `tagIds` on note create/update and `tagId` on note listing.** | Per proposal.md/spec: this is *input validation* on a field the caller controls (like an out-of-allowlist `sortBy`), not a resource-path lookup like `GET /api/notes/{id}` — so it follows the `400`-on-invalid-query/body-value precedent (`NoteListQueryDto`'s `[Range]`/`[OptionalAllowedValues]`), not the `404`-identical-to-not-found precedent used for path-segment resource ids. One exception type serves both call sites (note tagIds validation, and the list's single `tagId`) since the failure shape is identical: "one or more supplied tag ids are not usable by this caller." |
| **`ReplaceTagsForNoteAsync` always does delete-existing + insert-new (never a computed diff), for both create (no existing rows) and update (full replacement).** | Spec: "`tagIds` SHALL fully replace the note's existing tag assignment." A delete-all/insert-new is the simplest possible implementation of "fully replace" and is trivially correct; a diffed insert/delete would only save a handful of round-trip statements at the note-tag-count scale this ticket operates at (FRS/SDS set no performance target here), so the added complexity isn't justified. |
| **Tag-count aggregation (`GetActiveNoteCountsAsync`) is a per-tag correlated subquery (`_dbContext.Tags.Select(t => new { t.Id, Count = _dbContext.NoteTags.Count(nt => nt.TagId == t.Id && _dbContext.Notes.Any(n => n.Id == nt.NoteId)) })`), relying on `Notes`' existing soft-delete `HasQueryFilter` to exclude deleted notes automatically — no manual `DeletedAt == null` check duplicated in this query.** | `_dbContext.Notes.Any(...)` always goes through `Note`'s global query filter (`n.DeletedAt == null`) unless `IgnoreQueryFilters()` is called, which it isn't here — so "active notes only" (FRS-TAG-004) falls out for free from the same mechanism `GetByIdAsync`/`GetPageForUserAsync` already rely on, instead of a second, easily-drifting copy of the soft-delete predicate. |
| **`GetPageForUserAsync` gains a sixth parameter, `Guid? tagId`, inserted before `CancellationToken` — extended in place again, not forked into a new method.** | Same precedent AB-1005's plan documented for this exact method: "AB-1004 deliberately shaped this method to be extended... without a breaking change... no reason to fork." The `tagId` filter is applied as an additional `.Where(...)` only when `tagId is Guid t`, so the no-filter path's generated SQL is unchanged from AB-1005. |
| **`NoteService` gains a dependency on `ITagRepository`** (to validate `tagIds`/`tagId` ownership before touching `NoteRepository`). | `AuthService` already depends on multiple repositories (`IUserRepository`, `IRefreshTokenRepository`, `IPasswordResetOtpRepository`) — an Application service coordinating more than one aggregate's repository is already this codebase's norm, not a new pattern. |
| **Tag color format (`#RRGGBB`) is validated with the built-in `[RegularExpression]` attribute, not a new hand-rolled attribute class.** | Unlike `TrimmedLengthAttribute`/`OptionalAllowedValuesAttribute` (both written by hand because no built-in attribute expresses "valid after trimming" or "allowlist that tolerates null"), `RegularExpressionAttribute.IsValid(null)` already returns `true` (documented .NET behavior, consistent with this codebase's "null is `[Required]`'s concern" rule) — so the built-in attribute needs no wrapper here. |

## 2. Files to Create

### Domain (`apps/api/src/NoteManagement.Domain/Entities`)

**`Tag.cs`** (NEW) — mirrors `Note.cs`'s shape exactly (private setters, static factory, zero EF Core/ASP.NET Core dependency):
```csharp
namespace NoteManagement.Domain.Entities;

/// <summary>A user's tag (AB-1006 / FRS-TAG-001..004, SDS §15). Zero ASP.NET Core dependency.</summary>
public sealed class Tag
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Color { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Tag()
    {
    }

    public static Tag Create(Guid userId, string name, string color)
    {
        var now = DateTime.UtcNow;
        return new Tag
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Color = color,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>FRS-TAG-002: full replace of name/color; bumps UpdatedAt. Ownership (UserId) is never touched here.</summary>
    public void Rename(string name, string color)
    {
        Name = name;
        Color = color;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

**`NoteTag.cs`** (NEW) — the join row (SDS §16); no timestamps, no surrogate id:
```csharp
namespace NoteManagement.Domain.Entities;

/// <summary>
/// A note-tag association (AB-1006 / SDS §16). Composite identity is (NoteId, TagId) — this
/// class carries no other state and no independent lifecycle of its own.
/// </summary>
public sealed class NoteTag
{
    public Guid NoteId { get; private set; }
    public Guid TagId { get; private set; }

    private NoteTag()
    {
    }

    public static NoteTag Create(Guid noteId, Guid tagId) => new() { NoteId = noteId, TagId = tagId };
}
```

### Application — DTOs (`apps/api/src/NoteManagement.Application/DTOs`)

**`Tags/CreateTagRequestDto.cs`** (NEW):
```csharp
using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Tags;

/// <summary>FRS-TAG-001. Field shapes match delta-openapi.yaml's CreateTagRequest exactly.</summary>
public sealed record CreateTagRequestDto(
    [Required, TrimmedLength(1, 50)] string Name,
    [Required, RegularExpression(@"^#[0-9A-Fa-f]{6}$", ErrorMessage = "color must be a #RRGGBB hex value.")] string Color);
```

**`Tags/UpdateTagRequestDto.cs`** (NEW) — identical shape to create (FRS-TAG-002: same validation as create):
```csharp
using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Tags;

public sealed record UpdateTagRequestDto(
    [Required, TrimmedLength(1, 50)] string Name,
    [Required, RegularExpression(@"^#[0-9A-Fa-f]{6}$", ErrorMessage = "color must be a #RRGGBB hex value.")] string Color);
```

**`Tags/TagResponseDto.cs`** (NEW):
```csharp
namespace NoteManagement.Application.DTOs.Tags;

/// <summary>FRS-TAG-001..004. The shape returned by create/list/update.</summary>
public sealed record TagResponseDto(Guid Id, string Name, string Color, int NoteCount, DateTime CreatedAt, DateTime UpdatedAt);
```

**`Tags/TagRefDto.cs`** (NEW) — the minimal shape embedded in `NoteResponseDto.Tags`:
```csharp
namespace NoteManagement.Application.DTOs.Tags;

/// <summary>The tag shape embedded in a note's `tags` array — no noteCount, no timestamps.</summary>
public sealed record TagRefDto(Guid Id, string Name, string Color);
```

### Application — Interfaces

**`Interfaces/ITagRepository.cs`** (NEW):
```csharp
using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Interfaces;

/// <summary>Ownership (UserId) is baked into every lookup, same precedent as INoteRepository.</summary>
public interface ITagRepository
{
    void Add(Tag tag);

    void Remove(Tag tag);

    Task<Tag?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tag>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>FRS-TAG-004: tagId -&gt; count of the owner's active (non-deleted) notes carrying it. Every tag owned by userId appears in the result, including with a count of 0 when it currently carries no active notes.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveNoteCountsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Returns the subset of tagIds that exist and are owned by userId — callers diff this against what they submitted to find invalid ids (never reveals *why* an id was rejected).</summary>
    Task<IReadOnlyList<Guid>> GetOwnedIdsAsync(Guid userId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);
}
```

**`Interfaces/ITagService.cs`** (NEW):
```csharp
using NoteManagement.Application.DTOs.Tags;

namespace NoteManagement.Application.Interfaces;

public interface ITagService
{
    Task<TagResponseDto> CreateAsync(Guid userId, CreateTagRequestDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<TagResponseDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<TagResponseDto> UpdateAsync(Guid userId, Guid tagId, UpdateTagRequestDto request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, Guid tagId, CancellationToken cancellationToken);
}
```

**`Interfaces/INoteRepository.cs`** (MODIFY) — add three members, extend `GetPageForUserAsync`:
```csharp
Task<IReadOnlyList<Tag>> GetTagsForNoteAsync(Guid noteId, CancellationToken cancellationToken);

/// <summary>Batched form of GetTagsForNoteAsync for GET /api/notes — avoids one query per row.</summary>
Task<IReadOnlyDictionary<Guid, IReadOnlyList<Tag>>> GetTagsForNotesAsync(IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken);

/// <summary>Deletes every existing NoteTags row for noteId and inserts one per (already-validated, already-deduplicated) id in tagIds. Staged on the same DbContext as the note write — persisted together by the caller's single SaveChangesAsync.</summary>
Task ReplaceTagsForNoteAsync(Guid noteId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);
```
- `GetPageForUserAsync(Guid userId, int page, int pageSize, string sortBy, string sortDirection, CancellationToken cancellationToken)`
  → `GetPageForUserAsync(Guid userId, int page, int pageSize, string sortBy, string sortDirection, Guid? tagId, CancellationToken cancellationToken)`

### Application — Exceptions (`apps/api/src/NoteManagement.Application/Exceptions`)

**`TagNotFoundException.cs`** (NEW) — mirrors `NoteNotFoundException` exactly:
```csharp
namespace NoteManagement.Application.Exceptions;

/// <summary>Thrown when a tag doesn't exist or isn't owned by the caller — same exception for both so 404 never discloses which. Mapped to 404 by ProblemDetailsExceptionHandler.</summary>
public sealed class TagNotFoundException : Exception
{
    public TagNotFoundException(Guid tagId)
        : base($"Tag '{tagId}' was not found.")
    {
    }
}
```

**`DuplicateTagNameException.cs`** (NEW) — mirrors `DuplicateEmailException`:
```csharp
namespace NoteManagement.Application.Exceptions;

/// <summary>Thrown by TagService.CreateAsync/UpdateAsync when the (case-insensitive) name collides with another tag owned by the same user. Mapped to 409 by ProblemDetailsExceptionHandler.</summary>
public sealed class DuplicateTagNameException : Exception
{
    public DuplicateTagNameException(string name, Exception? innerException = null)
        : base($"Tag name '{name}' is already in use.", innerException)
    {
    }
}
```

**`InvalidTagReferenceException.cs`** (NEW):
```csharp
namespace NoteManagement.Application.Exceptions;

/// <summary>
/// Thrown when one or more supplied tag ids (Note create/update's tagIds, or the notes list's
/// tagId filter) do not exist or are not owned by the caller. Mapped to 400 — this is input
/// validation, not a resource lookup, so it is never 404 (see plan.md architecture decisions).
/// </summary>
public sealed class InvalidTagReferenceException : Exception
{
    public InvalidTagReferenceException(IReadOnlyCollection<Guid> invalidTagIds)
        : base($"The following tag ids do not exist or are not owned by the caller: {string.Join(", ", invalidTagIds)}.")
    {
    }
}
```

### Application — Services (MODIFY existing, ADD new)

**`Services/TagService.cs`** (NEW):
```csharp
using NoteManagement.Application.DTOs.Tags;
using NoteManagement.Application.Exceptions;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Services;

public sealed class TagService : ITagService
{
    private readonly ITagRepository _tagRepository;
    private readonly IUnitOfWork _unitOfWork;

    public TagService(ITagRepository tagRepository, IUnitOfWork unitOfWork)
    {
        _tagRepository = tagRepository;
        _unitOfWork = unitOfWork;
    }

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

    public async Task<IReadOnlyList<TagResponseDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tags = await _tagRepository.GetAllForUserAsync(userId, cancellationToken);
        var counts = await _tagRepository.GetActiveNoteCountsAsync(userId, cancellationToken);
        return tags.Select(t => Map(t, counts.GetValueOrDefault(t.Id))).ToList();
    }

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

    public async Task DeleteAsync(Guid userId, Guid tagId, CancellationToken cancellationToken)
    {
        var tag = await _tagRepository.GetByIdAsync(tagId, userId, cancellationToken)
            ?? throw new TagNotFoundException(tagId);

        // NoteTagConfiguration's FK cascade removes this tag's NoteTags rows automatically —
        // no manual association cleanup needed here (FRS-TAG-003: notes themselves are untouched).
        await _unitOfWork.RunInTransactionAsync(async ct =>
        {
            _tagRepository.Remove(tag);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    private static TagResponseDto Map(Tag tag, int noteCount) =>
        new(tag.Id, tag.Name, tag.Color, noteCount, tag.CreatedAt, tag.UpdatedAt);
}
```

**`Services/NoteService.cs`** (MODIFY) — add `ITagRepository` dependency; extend `CreateAsync`/`UpdateAsync`/`GetByIdAsync`/`ListAsync`/`RestoreAsync`; add a private `ResolveTagIdsAsync` helper:
```csharp
private readonly ITagRepository _tagRepository; // new constructor parameter, alongside noteRepository/unitOfWork

public async Task<NoteResponseDto> CreateAsync(Guid userId, CreateNoteRequestDto request, CancellationToken cancellationToken)
{
    var tagIds = await ResolveTagIdsAsync(userId, request.TagIds, cancellationToken);
    var note = Note.Create(userId, request.Title.Trim(), request.Content.Trim());

    await _unitOfWork.RunInTransactionAsync(async ct =>
    {
        _noteRepository.Add(note);
        await _noteRepository.ReplaceTagsForNoteAsync(note.Id, tagIds, ct);
        await _unitOfWork.SaveChangesAsync(ct); // single SaveChanges — EF Core orders the Note insert before its NoteTags rows automatically (same tracked graph)
    }, cancellationToken);

    return await MapWithTagsAsync(note, cancellationToken);
}

public async Task<NoteResponseDto> GetByIdAsync(Guid userId, Guid noteId, CancellationToken cancellationToken)
{
    var note = await _noteRepository.GetByIdAsync(noteId, userId, cancellationToken)
        ?? throw new NoteNotFoundException(noteId);
    return await MapWithTagsAsync(note, cancellationToken);
}

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

// DeleteAsync: unchanged (tag associations are preserved through soft delete/restore — no NoteTags touch needed).

public async Task<NoteResponseDto> RestoreAsync(Guid userId, Guid noteId, CancellationToken cancellationToken)
{
    // ... unchanged lookup/validation ...
    return await MapWithTagsAsync(note, cancellationToken);
}

/// <summary>Empty/missing tagIds -&gt; no assignment. Any id not owned by userId fails the whole request (proposal.md: no partial assignment).</summary>
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
```

### Application — DTOs (MODIFY existing)

**`DTOs/Notes/CreateNoteRequestDto.cs`** (MODIFY) — add optional `TagIds`:
```csharp
public sealed record CreateNoteRequestDto(
    [Required, TrimmedLength(1, 200)] string Title,
    [Required, TrimmedLength(1, int.MaxValue)] string Content,
    IReadOnlyList<Guid>? TagIds = null);
```

**`DTOs/Notes/UpdateNoteRequestDto.cs`** (MODIFY) — same addition:
```csharp
public sealed record UpdateNoteRequestDto(
    [Required, TrimmedLength(1, 200)] string Title,
    [Required, TrimmedLength(1, int.MaxValue)] string Content,
    IReadOnlyList<Guid>? TagIds = null);
```

**`DTOs/Notes/NoteResponseDto.cs`** (MODIFY) — insert `Tags` before the timestamps (matches delta-openapi.yaml's `Note` schema field order):
```csharp
public sealed record NoteResponseDto(Guid Id, string Title, string Content, IReadOnlyList<TagRefDto> Tags, DateTime CreatedAt, DateTime UpdatedAt);
```

**`DTOs/Notes/NoteListQueryDto.cs`** (MODIFY) — add `TagId`; no DataAnnotations needed (a malformed GUID string is already rejected `400` by `[ApiController]`'s automatic model-binding-failure handling, before this DTO's properties are even populated):
```csharp
public sealed record NoteListQueryDto(
    [Range(1, int.MaxValue, ErrorMessage = "page must be a positive integer.")] int? Page = null,
    [Range(1, int.MaxValue, ErrorMessage = "pageSize must be a positive integer.")] int? PageSize = null,
    [OptionalAllowedValues("createdAt", "updatedAt", "title", ErrorMessage = "sortBy must be one of: createdAt, updatedAt, title.")] string? SortBy = null,
    [OptionalAllowedValues("asc", "desc", ErrorMessage = "sortDirection must be one of: asc, desc.")] string? SortDirection = null,
    Guid? TagId = null);
```

### Application — DI

**`DependencyInjection.cs`** (MODIFY) — register `ITagService`:
```csharp
services.AddScoped<ITagService, TagService>();
```

### Infrastructure — Configurations (`apps/api/src/NoteManagement.Infrastructure/Configurations`)

**`TagConfiguration.cs`** (NEW):
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Configurations;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(50).IsRequired();

        // Fixed-width #RRGGBB — 7 characters. No format CHECK constraint at the DB layer, same
        // precedent as Note.Content: format validation lives at the Application layer
        // (CreateTagRequestDto's [RegularExpression]), not duplicated here.
        builder.Property(t => t.Color).HasMaxLength(7).IsRequired();

        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt).IsRequired();

        // FRS-TAG-001 case-insensitive per-user uniqueness relies on the database's default
        // collation (case-insensitive) — the same reliance UserConfiguration's Users.Email index
        // already makes; see plan.md architecture decisions. This composite index's leftmost
        // column (UserId) already serves a UserId-only lookup, so no separate solo index is added.
        builder.HasIndex(t => new { t.UserId, t.Name }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**`NoteTagConfiguration.cs`** (NEW):
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Infrastructure.Configurations;

public sealed class NoteTagConfiguration : IEntityTypeConfiguration<NoteTag>
{
    public void Configure(EntityTypeBuilder<NoteTag> builder)
    {
        builder.ToTable("NoteTags");
        builder.HasKey(nt => new { nt.NoteId, nt.TagId });

        // Tag -> NoteTags cascades: deleting a Tag (TagService.DeleteAsync) removes its NoteTags
        // rows without app code (FRS-TAG-003). Note -> NoteTags is Restrict, not Cascade —
        // **corrected during implementation**: SQL Server rejected the originally-planned
        // Cascade/Cascade pair with "may cause cycles or multiple cascade paths" (Users -> Notes
        // -> NoteTags and Users -> Tags -> NoteTags both cascade-delete NoteTags from the same
        // ancestor, which SQL Server disallows), caught by actually applying the migration, not
        // by static review. Restrict is safe today because nothing hard-deletes a Note yet — soft
        // delete is an UPDATE, not a DELETE (SDS §14) — but a future hard-purge process (no ticket
        // owns one yet) will need to delete a note's NoteTags rows itself before deleting the note.
        builder.HasOne<Note>().WithMany().HasForeignKey(nt => nt.NoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tag>().WithMany().HasForeignKey(nt => nt.TagId).OnDelete(DeleteBehavior.Cascade);

        // The composite PK already indexes (NoteId, TagId) — a secondary index on TagId alone
        // supports GetActiveNoteCountsAsync's per-tag lookups (AGENTS.md §9).
        builder.HasIndex(nt => nt.TagId);

        // No HasQueryFilter here: association rows must remain visible even for a soft-deleted
        // note (Notes' own filter already hides the note itself), so a restored note keeps its tags.
    }
}
```

### Infrastructure — DbContext

**`Data/ApplicationDbContext.cs`** (MODIFY) — add two `DbSet`s:
```csharp
public DbSet<Tag> Tags => Set<Tag>();

public DbSet<NoteTag> NoteTags => Set<NoteTag>();
```

### Infrastructure — Repositories

**`Repositories/TagRepository.cs`** (NEW):
```csharp
using Microsoft.EntityFrameworkCore;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;
using NoteManagement.Infrastructure.Data;

namespace NoteManagement.Infrastructure.Repositories;

public sealed class TagRepository : ITagRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TagRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(Tag tag) => _dbContext.Tags.Add(tag);

    public void Remove(Tag tag) => _dbContext.Tags.Remove(tag);

    public Task<Tag?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Tags.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Tag>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _dbContext.Tags.Where(t => t.UserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetActiveNoteCountsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // _dbContext.Notes.Any(...) goes through Note's global query filter (DeletedAt == null)
        // automatically — "active notes only" needs no separate predicate here (FRS-TAG-004).
        var counts = await _dbContext.Tags
            .Where(t => t.UserId == userId)
            .Select(t => new
            {
                t.Id,
                Count = _dbContext.NoteTags.Count(nt => nt.TagId == t.Id && _dbContext.Notes.Any(n => n.Id == nt.NoteId)),
            })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(c => c.Id, c => c.Count);
    }

    public async Task<IReadOnlyList<Guid>> GetOwnedIdsAsync(Guid userId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        return await _dbContext.Tags
            .Where(t => t.UserId == userId && tagIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
    }
}
```

**`Repositories/NoteRepository.cs`** (MODIFY) — add three methods, extend `GetPageForUserAsync`:
```csharp
public async Task<IReadOnlyList<Tag>> GetTagsForNoteAsync(Guid noteId, CancellationToken cancellationToken) =>
    await _dbContext.NoteTags
        .Where(nt => nt.NoteId == noteId)
        .Join(_dbContext.Tags, nt => nt.TagId, t => t.Id, (nt, t) => t)
        .ToListAsync(cancellationToken);

public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Tag>>> GetTagsForNotesAsync(IReadOnlyCollection<Guid> noteIds, CancellationToken cancellationToken)
{
    if (noteIds.Count == 0)
    {
        return new Dictionary<Guid, IReadOnlyList<Tag>>();
    }

    var rows = await _dbContext.NoteTags
        .Where(nt => noteIds.Contains(nt.NoteId))
        .Join(_dbContext.Tags, nt => nt.TagId, t => t.Id, (nt, t) => new { nt.NoteId, Tag = t })
        .ToListAsync(cancellationToken);

    return rows.GroupBy(r => r.NoteId).ToDictionary(g => g.Key, g => (IReadOnlyList<Tag>)g.Select(r => r.Tag).ToList());
}

public async Task ReplaceTagsForNoteAsync(Guid noteId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
{
    var existing = await _dbContext.NoteTags.Where(nt => nt.NoteId == noteId).ToListAsync(cancellationToken);
    _dbContext.NoteTags.RemoveRange(existing);

    foreach (var tagId in tagIds)
    {
        _dbContext.NoteTags.Add(NoteTag.Create(noteId, tagId));
    }
}

public async Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, string sortBy, string sortDirection, Guid? tagId, CancellationToken cancellationToken)
{
    var query = _dbContext.Notes.Where(n => n.UserId == userId);

    if (tagId is Guid t)
    {
        query = query.Where(n => _dbContext.NoteTags.Any(nt => nt.NoteId == n.Id && nt.TagId == t));
    }

    var totalCount = await query.CountAsync(cancellationToken);
    // ... unchanged (sortBy, sortDirection) switch + Skip/Take ...
}
```

### Infrastructure — DI

**`DependencyInjection.cs`** (MODIFY) — register `ITagRepository`:
```csharp
// AB-1006: tags persistence.
services.AddScoped<ITagRepository, TagRepository>();
```

### Api — Controllers

**`Controllers/TagsController.cs`** (NEW):
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoteManagement.Api.Extensions;
using NoteManagement.Application.DTOs.Tags;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Api.Controllers;

[ApiController]
[Route("api/tags")]
[Authorize]
public sealed class TagsController : ControllerBase
{
    private readonly ITagService _tagService;

    public TagsController(ITagService tagService)
    {
        _tagService = tagService;
    }

    /// <summary>FRS-TAG-001.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(TagResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TagResponseDto>> Create(CreateTagRequestDto request, CancellationToken cancellationToken)
    {
        var tag = await _tagService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, tag);
    }

    /// <summary>FRS-TAG-004.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TagResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TagResponseDto>>> List(CancellationToken cancellationToken)
    {
        var tags = await _tagService.ListAsync(User.GetUserId(), cancellationToken);
        return Ok(tags);
    }

    /// <summary>FRS-TAG-002.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TagResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TagResponseDto>> Update(Guid id, UpdateTagRequestDto request, CancellationToken cancellationToken)
    {
        var tag = await _tagService.UpdateAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(tag);
    }

    /// <summary>FRS-TAG-003.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _tagService.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }
}
```

**`Controllers/NotesController.cs`** — no signature changes required; `Create`/`Update`/`List` already bind their request DTOs wholesale (`CreateNoteRequestDto`, `UpdateNoteRequestDto`, `[FromQuery] NoteListQueryDto`), so the new `TagIds`/`TagId` members flow through automatically. Add `[ProducesResponseType(StatusCodes.Status409Conflict)]`? — **not needed**: `409` on notes endpoints is unchanged (only tag-name conflicts are `409`, and those live on `TagsController`).

### Api — Middleware

**`Middleware/ProblemDetailsExceptionHandler.cs`** (MODIFY) — extend the `switch`:
```csharp
TagNotFoundException => (StatusCodes.Status404NotFound, "Tag not found"),
DuplicateTagNameException => (StatusCodes.Status409Conflict, "Duplicate tag name"),
InvalidTagReferenceException => (StatusCodes.Status400BadRequest, "Invalid tag reference"),
```

### Infrastructure — EF Core Migration

```bash
dotnet ef migrations add AddTagsAndNoteTags --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
```

Expected `Up()`: `CreateTable "Tags"` (`Id` PK, `UserId` FK→Users cascade, `Name` nvarchar(50) not null, `Color` nvarchar(7) not null, `CreatedAt`/`UpdatedAt` datetime2 not null) + unique index `IX_Tags_UserId_Name`; `CreateTable "NoteTags"` (`NoteId` FK→Notes cascade, `TagId` FK→Tags cascade, composite PK) + index `IX_NoteTags_TagId`. Purely additive — no existing table/column is altered, so this migration is backward compatible (an already-running instance of AB-1005's schema upgrades cleanly; no data migration needed since both new tables start empty).

### Shared TypeScript contracts (`packages/shared`)

**`packages/shared/src/schemas/tags.ts`** (NEW):
```typescript
// Zod schemas (AB-1006) — validation mirrors of the backend's Tag DTOs, for frontend UX
// convenience only (packages/shared/CLAUDE.md). The backend remains authoritative.
import { z } from 'zod';

export const createTagRequestSchema = z.object({
  name: z.string().trim().min(1).max(50),
  color: z.string().regex(/^#[0-9A-Fa-f]{6}$/),
});
export type CreateTagRequest = z.infer<typeof createTagRequestSchema>;

export const updateTagRequestSchema = createTagRequestSchema;
export type UpdateTagRequest = z.infer<typeof updateTagRequestSchema>;

export const tagResponseSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  color: z.string(),
  noteCount: z.number().int(),
  createdAt: z.string(),
  updatedAt: z.string(),
});
export type TagResponse = z.infer<typeof tagResponseSchema>;

// GET /api/tags returns a plain array — no pagination envelope (proposal.md).
export const tagListResponseSchema = z.array(tagResponseSchema);
export type TagListResponse = z.infer<typeof tagListResponseSchema>;

// The minimal shape embedded in a note's `tags` array.
export const tagRefSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  color: z.string(),
});
export type TagRef = z.infer<typeof tagRefSchema>;
```

**`packages/shared/src/types/tags.ts`** (NEW) — re-export, same idiom as `types/notes.ts`:
```typescript
export type {
  CreateTagRequest,
  UpdateTagRequest,
  TagResponse,
  TagListResponse,
  TagRef,
} from '../schemas/tags';
```

**`packages/shared/src/schemas/notes.ts`** (MODIFY) — add `tagIds`/`tags`/`tagId`, importing `tagRefSchema`:
```typescript
import { tagRefSchema } from './tags';

export const createNoteRequestSchema = z.object({
  title: z.string().trim().min(1).max(200),
  content: z.string().trim().min(1),
  tagIds: z.array(z.string().uuid()).optional(),
});
// updateNoteRequestSchema = createNoteRequestSchema (unchanged assignment)

export const noteResponseSchema = z.object({
  id: z.string().uuid(),
  title: z.string(),
  content: z.string(),
  tags: z.array(tagRefSchema),
  createdAt: z.string(),
  updatedAt: z.string(),
});

export const noteListQuerySchema = z.object({
  page: z.number().int().min(1).optional(),
  pageSize: z.number().int().min(1).optional(),
  sortBy: z.enum(['createdAt', 'updatedAt', 'title']).optional(),
  sortDirection: z.enum(['asc', 'desc']).optional(),
  tagId: z.string().uuid().optional(),
});
```

**`packages/shared/src/index.ts`** (MODIFY) — add the tags export block (mirrors the existing notes block):
```typescript
// AB-1006 — Tag DTOs + Zod schemas (SDS §55/§81). Consumed starting AB-1011/AB-1012.
export {
  createTagRequestSchema,
  updateTagRequestSchema,
  tagResponseSchema,
  tagListResponseSchema,
  tagRefSchema,
} from './schemas/tags';
export * from './types/tags';
```
No Vitest test required for the new schemas — plain `z.object`/`z.array` shape mirrors with no custom logic, same precedent `noteListResponseSchema`/`noteListQuerySchema` already established (AB-1005 plan.md §2).

## 3. Test Plan

One test per new/changed scenario (AGENTS.md §10 / SDS §76), split unit vs. integration per the established convention: pure business-rule/mapping logic → `Tests.Unit` (hand-rolled fakes, no mocking library, per `NoteServiceTests`' precedent); anything where `[ApiController]`'s automatic ModelState→400 or the real HTTP/auth pipeline matters → `Tests.Integration` (`WebApplicationFactory`, isolated LocalDB database per test class, per `NotesControllerTests`' precedent).

### Domain — `Tests.Unit/Domain/TagTests.cs` (NEW, mirrors `NoteTests.cs`)
- `Create_SetsAllFields`
- `Rename_UpdatesNameColorAndUpdatedAt_LeavesOwnerUnchanged`

### Domain — `Tests.Unit/Domain/NoteTagTests.cs` (NEW)
- `Create_SetsNoteIdAndTagId`

### `tags` capability

| Spec scenario | Test(s) |
|---|---|
| Successful tag creation | `TagServiceTests.CreateAsync_WithValidData_CreatesTag`; `TagsControllerTests.Create_WithValidData_Returns201WithTag` |
| Missing name rejected | `TagsControllerTests.Create_WithMissingOrBlankName_Returns400` |
| Name exceeding maximum length rejected | `TagsControllerTests.Create_WithNameOver50Chars_Returns400` |
| Missing color rejected | `TagsControllerTests.Create_WithMissingColor_Returns400` |
| Invalid color format rejected | `TagsControllerTests.Create_WithInvalidColorFormat_Returns400` (table: no `#`, wrong digit count, non-hex chars, named color) |
| Duplicate name for the same user rejected | `TagServiceTests.CreateAsync_WithCaseInsensitiveDuplicateName_ThrowsDuplicateTagNameException`; `TagsControllerTests.Create_WithDuplicateName_Returns409` |
| Same name allowed across different users | `TagsControllerTests.Create_SameNameDifferentUsers_BothSucceed` |
| Unauthenticated request rejected | `TagsControllerTests.Create_WithoutAccessToken_Returns401` |
| Lists only the caller's own tags | `TagsControllerTests.List_ReturnsOnlyCallersTags` |
| Empty list for a user with no tags | `TagServiceTests.ListAsync_WithNoTags_ReturnsEmptyArray` |
| Note count reflects only active notes | `TagsControllerTests.List_NoteCountExcludesSoftDeletedNotes` |
| Another user's tags excluded from the list | `TagsControllerTests.List_ExcludesOtherUsersTags` |
| Unauthenticated request rejected (list) | `TagsControllerTests.List_WithoutAccessToken_Returns401` |
| Owner successfully updates their tag | `TagServiceTests.UpdateAsync_WithValidData_UpdatesNameAndColor`; `TagsControllerTests.Update_WithValidData_Returns200` |
| Update to unchanged name allowed | `TagsControllerTests.Update_WithSameNameNewColor_Returns200` |
| Invalid update rejected | `TagsControllerTests.Update_WithInvalidNameOrColor_Returns400` |
| Update to a duplicate name rejected | `TagServiceTests.UpdateAsync_WithDuplicateName_ThrowsDuplicateTagNameException`; `TagsControllerTests.Update_WithDuplicateName_Returns409` |
| Update to non-existent tag rejected | `TagServiceTests.UpdateAsync_WithUnknownId_ThrowsTagNotFoundException` |
| Update to another user's tag rejected identically to not-found | `TagsControllerTests.Update_OtherUsersTag_Returns404` |
| Owner deletes their tag | `TagServiceTests.DeleteAsync_WithOwnedTag_RemovesTag`; `TagsControllerTests.Delete_WithOwnedTag_Returns204` |
| Deleting a tag preserves its notes | `TagsControllerTests.Delete_PreservesAssociatedNotesButRemovesAssociation` |
| Deleted tag excluded from subsequent listing | `TagsControllerTests.Delete_ThenList_ExcludesDeletedTag` |
| Delete of non-existent tag rejected | `TagServiceTests.DeleteAsync_WithUnknownId_ThrowsTagNotFoundException` |
| Delete of another user's tag rejected identically to not-found | `TagsControllerTests.Delete_OtherUsersTag_Returns404` |

### `notes` capability — deltas only (existing unchanged scenarios' tests are untouched)

| Spec scenario | Test(s) |
|---|---|
| Successful note creation with tags | `NoteServiceTests.CreateAsync_WithTagIds_AssociatesNoteWithTags`; `NotesControllerTests.Create_WithTagIds_Returns201WithTags` |
| Duplicate tag ids de-duplicated | `NoteServiceTests.CreateAsync_WithDuplicateTagIds_AssignsTagExactlyOnce` |
| Non-existent or unowned tag id rejected (create) | `NoteServiceTests.CreateAsync_WithInvalidTagId_ThrowsInvalidTagReferenceException`; `NotesControllerTests.Create_WithInvalidTagId_Returns400` |
| Owner successfully updates their note (tags) | `NoteServiceTests.UpdateAsync_WithTagIds_ReplacesTagAssignment`; `NotesControllerTests.Update_WithTagIds_Returns200WithUpdatedTags` |
| Omitted tag no longer associated after update | `NoteServiceTests.UpdateAsync_OmittingPreviouslyAssignedTag_RemovesAssociation` |
| Empty tagIds clears all tag assignments | `NoteServiceTests.UpdateAsync_WithEmptyTagIds_ClearsAllAssignments` |
| Non-existent or unowned tag id rejected (update) | `NoteServiceTests.UpdateAsync_WithInvalidTagId_ThrowsInvalidTagReferenceException`; `NotesControllerTests.Update_WithInvalidTagId_Returns400` |
| Client filters by a tag they own | `NoteServiceTests.ListAsync_WithTagIdFilter_ReturnsOnlyNotesCarryingThatTag`; `NotesControllerTests.List_WithTagIdFilter_ReturnsFilteredNotes` |
| Filtering by a tag with no matching notes returns an empty page | `NotesControllerTests.List_WithTagIdFilterNoMatches_ReturnsEmptyItems` |
| Non-existent or unowned tagId rejected | `NoteServiceTests.ListAsync_WithInvalidTagId_ThrowsInvalidTagReferenceException`; `NotesControllerTests.List_WithInvalidTagId_Returns400` |
| (regression) Lists only the caller's active notes | `NotesControllerTests.List_ReturnsOwnedNotesWithPaginationEnvelope` extended to assert each item's `tags` field is present (empty array when none assigned) |
| (regression) Single note retrieval / restore include tags | `NotesControllerTests.GetById_WithOwnedNote_Returns200` and `Restore_WithDeletedNote_Returns200` extended to assert `tags` reflects current assignment |

**Fakes to update** (`NoteServiceTests.FakeNoteRepository`): add `GetTagsForNoteAsync`/`GetTagsForNotesAsync`/`ReplaceTagsForNoteAsync` (an in-memory `Dictionary<Guid, List<Guid>>` of noteId→tagIds, resolved against a `FakeTagRepository`'s in-memory tag list for name/color), extend `GetPageForUserAsync`'s signature with `tagId`, and mirror the repository's tagId filter with a plain `.Where(...)`. A new `FakeTagRepository` (implementing `ITagRepository`) backs both `NoteServiceTests` (tag-ownership validation) and the new `TagServiceTests`.

## 4. Checkpoint Commands

Run in order; fix and re-run on the first failure before proceeding (root `CLAUDE.md` Quality Gates).

**Backend** (per `apps/api/CLAUDE.md`):
```bash
dotnet build
dotnet test
```

**Shared package / monorepo-wide** (this ticket touches `packages/shared`; no `apps/web` diff, but AGENTS.md §12/§4 gates apply to any TS change regardless):
```bash
pnpm lint --max-warnings 0
pnpm build
pnpm test
```
`pnpm test --coverage` and `dotnet test --collect:"XPlat Code Coverage"` before marking the ticket complete (root `CLAUDE.md` Quality Gates / AGENTS.md §4 — ≥80% coverage on new code).

```bash
dotnet ef migrations add AddTagsAndNoteTags --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
```

## 5. Explicitly Out of Scope (unchanged from proposal.md)

Multi-tag filtering (AND/OR across several `tagId` values); tag colors beyond `#RRGGBB` hex; any frontend tag UI (AB-1011/AB-1012); search (AB-1007); sharing (AB-1008); version history capturing tag assignments (AB-1009 — `NoteVersions` snapshots title/content only).
