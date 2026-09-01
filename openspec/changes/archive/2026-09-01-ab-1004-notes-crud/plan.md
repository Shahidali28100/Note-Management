
# Plan: ab-1004-notes-crud

Source: `proposal.md`, `specs/notes/spec.md`, `delta-openapi.yaml`, `docs/SDS.md`, `AGENTS.md`, `apps/api/CLAUDE.md`. Introduces a new `notes` capability — new `NotesController`/`NoteService`/`Note` entity, parallel to (not touching) `AuthController`/`AuthService` from AB-1002/AB-1003.

## 0. Reused facts from AB-1001/AB-1002/AB-1003 (already resolved, not re-litigated)

- `.NET SDK`/LocalDB/`dotnet-ef` environment, Central Package Management (`apps/api/Directory.Packages.props`), `packages/shared`'s no-build-step TS — all unchanged.
- Layering/DI/testing template: `AddApplication()`/`AddInfrastructure(configuration)`, hand-rolled test fakes (no Moq/NSubstitute), fail-fast `?? throw` config reads, `[ApiController]` automatic 400 on invalid `ModelState`, `ProblemDetailsExceptionHandler`'s typed-exception-to-Problem-Details mapping — all reused as-is.
- `ApplicationDbContext`, `IUnitOfWork`/`UnitOfWork` (transaction + unique-constraint translation), JWT bearer auth pipeline (`[Authorize]`, `sub` claim = user id) — already exist, extended in place.
- `AuthController`/`AuthService`/`User`/`RefreshToken`/`PasswordResetOtp` are **not modified** by this ticket.

## 1. New package versions

None. `Note`/`NoteConfiguration`/`NoteRepository`/`NoteService` only need `System.ComponentModel.DataAnnotations` (BCL) and EF Core APIs already referenced by `NoteManagement.Application`/`NoteManagement.Infrastructure`. No `Directory.Packages.props` change.

## 2. File tree to create / modify

```
apps/api/
├── src/
│   ├── NoteManagement.Domain/
│   │   └── Entities/
│   │       └── Note.cs                                              (NEW)
│   │
│   ├── NoteManagement.Application/
│   │   ├── DTOs/Notes/
│   │   │   ├── CreateNoteRequestDto.cs                              (NEW)
│   │   │   ├── UpdateNoteRequestDto.cs                               (NEW)
│   │   │   ├── NoteResponseDto.cs                                    (NEW)
│   │   │   └── NoteListResponseDto.cs                                (NEW)
│   │   ├── Exceptions/
│   │   │   ├── NoteNotFoundException.cs                              (NEW)
│   │   │   └── NoteNotDeletedException.cs                            (NEW)
│   │   ├── Interfaces/
│   │   │   ├── INoteRepository.cs                                    (NEW)
│   │   │   └── INoteService.cs                                       (NEW)
│   │   ├── Validation/
│   │   │   └── TrimmedLengthAttribute.cs                             (NEW)
│   │   ├── Services/
│   │   │   └── NoteService.cs                                        (NEW)
│   │   └── DependencyInjection.cs                                     # MODIFIED — register INoteService
│   │
│   ├── NoteManagement.Infrastructure/
│   │   ├── Configurations/
│   │   │   └── NoteConfiguration.cs                                  (NEW)
│   │   ├── Data/
│   │   │   └── ApplicationDbContext.cs                                # MODIFIED — +DbSet<Note>
│   │   ├── Repositories/
│   │   │   └── NoteRepository.cs                                     (NEW)
│   │   ├── Migrations/
│   │   │   └── <timestamp>_AddNotes.cs                                (NEW, generated)
│   │   └── DependencyInjection.cs                                     # MODIFIED — register INoteRepository
│   │
│   └── NoteManagement.Api/
│       ├── Controllers/
│       │   └── NotesController.cs                                    (NEW)
│       ├── Extensions/
│       │   └── ClaimsPrincipalExtensions.cs                          (NEW — GetUserId(), used only by NotesController)
│       ├── Middleware/
│       │   └── ProblemDetailsExceptionHandler.cs                     # MODIFIED — map NoteNotFoundException → 404, NoteNotDeletedException → 409
│       └── Program.cs                                                # MODIFIED — AddSwaggerGen() gains a JWT Bearer security definition/requirement (§7a; pre-existing gap from AB-1002, surfaced now because this ticket's 5 protected endpoints need manual Swagger verification)
│
└── tests/
    ├── NoteManagement.Tests.Unit/
    │   ├── Domain/
    │   │   └── NoteTests.cs                                          (NEW)
    │   └── Application/
    │       └── NoteServiceTests.cs                                    (NEW)
    └── NoteManagement.Tests.Integration/
        └── Api/
            └── NotesControllerTests.cs                                (NEW)

packages/shared/
└── src/
    ├── schemas/notes.ts                                                (NEW)
    ├── types/notes.ts                                                  (NEW)
    └── index.ts                                                        # MODIFIED — export the new schemas/types
```

`appsettings*.json` are **unchanged** — `NotesController` reuses the JWT bearer scheme and CORS policy AB-1002 already wired up; no new configuration. `Program.cs` gets one small, user-requested addition (§7a): `AddSwaggerGen()` currently has no security definition, so Swagger UI has never had an Authorize button — true since AB-1002 introduced the first `[Authorize]` endpoint, silently carried through AB-1003, and now blocks manually verifying this ticket's 5 protected endpoints (task 3.6) the same way. Fixed here rather than deferred, since it blocks this ticket's own manual-verification step.

`AuthController.cs`'s inline `sub`-claim extraction (`AuthController.GetMe`) is left untouched — `ClaimsPrincipalExtensions.GetUserId()` is added net-new for `NotesController`'s five actions (which all need it, vs. `AuthController`'s single occurrence) rather than refactoring `AuthController` in a ticket that doesn't otherwise touch it.

## 3. Domain layer

```csharp
// Domain/Entities/Note.cs
namespace NoteManagement.Domain.Entities;

/// <summary>
/// A user's note (AB-1004 / FRS-NOTE-001..005, SDS §13). Content is stored as an opaque string —
/// no structural/format validation or interpretation (proposal.md: the TipTap representation is
/// an AB-1012 decision). Zero ASP.NET Core dependency.
/// </summary>
public sealed class Note
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    /// <summary>EF Core materialization only — invariants are enforced via <see cref="Create"/>.</summary>
    private Note()
    {
    }

    public static Note Create(Guid userId, string title, string content)
    {
        var now = DateTime.UtcNow;
        return new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>FRS-NOTE-003: full replace of title/content; bumps UpdatedAt, leaves CreatedAt untouched.</summary>
    public void UpdateContent(string title, string content)
    {
        Title = title;
        Content = content;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>FRS-NOTE-004. Idempotent at the domain level (mirrors RefreshToken.Revoke()) — NoteService still rejects a redundant call with NoteNotFoundException per the spec's "already-deleted → 404" scenario before this is ever reached twice for the same note.</summary>
    public void SoftDelete() => DeletedAt ??= DateTime.UtcNow;

    /// <summary>FRS-NOTE-005. NoteService checks IsDeleted before calling this (throws NoteNotDeletedException otherwise), so this is only ever invoked on a currently-deleted note.</summary>
    public void Restore() => DeletedAt = null;
}
```

## 4. Application layer

**Validation attribute** (new — no built-in attribute expresses "length after trimming"):

```csharp
// Application/Validation/TrimmedLengthAttribute.cs
using System.ComponentModel.DataAnnotations;

namespace NoteManagement.Application.Validation;

/// <summary>
/// spec "Note Creation"/"Note Update": valid only if, after trimming leading/trailing
/// whitespace, the value's length is within [minLength, maxLength] — so a whitespace-only
/// string is rejected even though its raw length is nonzero, unlike a plain
/// StringLength/MinLength pair. Mirrors PasswordPolicyAttribute's precedent of a small
/// hand-written composite check where the built-in attributes can't express the rule.
/// </summary>
public sealed class TrimmedLengthAttribute : ValidationAttribute
{
    private readonly int _minLength;
    private readonly int _maxLength;

    public TrimmedLengthAttribute(int minLength, int maxLength)
    {
        _minLength = minLength;
        _maxLength = maxLength;
        ErrorMessage = $"Value must be between {minLength} and {maxLength} characters after trimming whitespace.";
    }

    public override bool IsValid(object? value)
    {
        // Null is [Required]'s concern, not this attribute's — same precedent as PasswordPolicyAttribute.
        if (value is null)
        {
            return true;
        }

        if (value is not string s)
        {
            return false;
        }

        var trimmedLength = s.Trim().Length;
        return trimmedLength >= _minLength && trimmedLength <= _maxLength;
    }
}
```

**DTOs** (match `delta-openapi.yaml` field-for-field; attributes target constructor parameters, per `RegisterRequestDto`'s established remark):

```csharp
// Application/DTOs/Notes/CreateNoteRequestDto.cs
public sealed record CreateNoteRequestDto(
    [Required, TrimmedLength(1, 200)] string Title,
    [Required, TrimmedLength(1, int.MaxValue)] string Content);

// Application/DTOs/Notes/UpdateNoteRequestDto.cs
public sealed record UpdateNoteRequestDto(
    [Required, TrimmedLength(1, 200)] string Title,
    [Required, TrimmedLength(1, int.MaxValue)] string Content);

// Application/DTOs/Notes/NoteResponseDto.cs
public sealed record NoteResponseDto(Guid Id, string Title, string Content, DateTime CreatedAt, DateTime UpdatedAt);

// Application/DTOs/Notes/NoteListResponseDto.cs
public sealed record NoteListResponseDto(
    IReadOnlyList<NoteResponseDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
```

**Exceptions** (mapped to Problem Details by `ProblemDetailsExceptionHandler`, §7):

```csharp
// Application/Exceptions/NoteNotFoundException.cs
/// <summary>Thrown when a note doesn't exist, isn't owned by the caller, or (for non-restore lookups) is soft-deleted — same exception for all three so the 404 response never distinguishes them (spec: "no existence/ownership disclosure"). Mapped to 404.</summary>
public sealed class NoteNotFoundException : Exception
{
    public NoteNotFoundException(Guid noteId)
        : base($"Note '{noteId}' was not found.")
    {
    }
}

// Application/Exceptions/NoteNotDeletedException.cs
/// <summary>Thrown by RestoreAsync when the note exists and is owned by the caller but isn't currently soft-deleted. Mapped to 409.</summary>
public sealed class NoteNotDeletedException : Exception
{
    public NoteNotDeletedException(Guid noteId)
        : base($"Note '{noteId}' is not deleted; nothing to restore.")
    {
    }
}
```

**`INoteRepository`** — ownership (`UserId`) is baked into every lookup query rather than checked after the fact in the service, so a non-owned note is never loaded into memory at all (defense in depth beyond the spec's "404 either way" requirement; matches SDS §58 "resources scoped to the authenticated user"):

```csharp
// Application/Interfaces/INoteRepository.cs
public interface INoteRepository
{
    void Add(Note note);

    /// <summary>Active (non-deleted) note owned by userId — the global query filter (§5) already excludes soft-deleted rows. Backs GET/PUT/DELETE.</summary>
    Task<Note?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>Same ownership scoping as GetByIdAsync, but bypasses the soft-delete filter — the only lookup that can see a deleted note. Backs restore, which must distinguish "not found" from "found but not deleted."</summary>
    Task<Note?> GetByIdIncludingDeletedAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>Active notes owned by userId, sorted by UpdatedAt descending. AB-1004 always calls this with page=1/pageSize=20 (the fixed default view); the page/pageSize parameters exist now so AB-1005 can wire real query-string values through without changing this signature.</summary>
    Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);
}
```

**`INoteService`**:

```csharp
// Application/Interfaces/INoteService.cs
public interface INoteService
{
    Task<NoteResponseDto> CreateAsync(Guid userId, CreateNoteRequestDto request, CancellationToken cancellationToken);

    Task<NoteResponseDto> GetByIdAsync(Guid userId, Guid noteId, CancellationToken cancellationToken);

    Task<NoteListResponseDto> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<NoteResponseDto> UpdateAsync(Guid userId, Guid noteId, UpdateNoteRequestDto request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, Guid noteId, CancellationToken cancellationToken);

    Task<NoteResponseDto> RestoreAsync(Guid userId, Guid noteId, CancellationToken cancellationToken);
}
```

**`NoteService`** — every write wraps `SaveChangesAsync` in `_unitOfWork.RunInTransactionAsync`, matching `AuthService`'s convention for every mutation (not only genuinely multi-statement ones):

```csharp
// Application/Services/NoteService.cs
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
```

**`DependencyInjection.cs`** (Application) addition:

```csharp
services.AddScoped<INoteService, NoteService>();
```

## 5. Infrastructure layer

**EF Core configuration:**

```csharp
// Infrastructure/Configurations/NoteConfiguration.cs
public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title)
            .HasMaxLength(200)
            .IsRequired();

        // No HasMaxLength — nvarchar(max), opaque per the AB-1004 content-format decision
        // (proposal.md). No structural/format validation at this layer either.
        builder.Property(n => n.Content)
            .IsRequired();

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        builder.Property(n => n.UpdatedAt)
            .IsRequired();

        // DeletedAt is nullable by default — no IsRequired() call needed.

        // Supports GetPageForUserAsync's WHERE UserId = X (filter narrows to DeletedAt IS NULL)
        // ORDER BY UpdatedAt DESC, and GetByIdAsync/GetByIdIncludingDeletedAsync's
        // WHERE Id = X AND UserId = Y — same composite-index idiom as
        // PasswordResetOtpConfiguration's (UserId, UsedAt).
        builder.HasIndex(n => new { n.UserId, n.DeletedAt, n.UpdatedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // FRS-NOTE-004/SDS §14: normal queries exclude soft-deleted rows by default.
        // GetByIdIncludingDeletedAsync explicitly calls IgnoreQueryFilters() to see past this.
        builder.HasQueryFilter(n => n.DeletedAt == null);
    }
}
```

**`ApplicationDbContext`** gains:

```csharp
public DbSet<Note> Notes => Set<Note>();
```

(`OnModelCreating`'s `ApplyConfigurationsFromAssembly` call already picks up `NoteConfiguration` automatically.)

**`NoteRepository`:**

```csharp
// Infrastructure/Repositories/NoteRepository.cs
public sealed class NoteRepository : INoteRepository
{
    private readonly ApplicationDbContext _dbContext;

    public NoteRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(Note note) => _dbContext.Notes.Add(note);

    public Task<Note?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Notes.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public Task<Note?> GetByIdIncludingDeletedAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Notes.IgnoreQueryFilters().SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public async Task<(IReadOnlyList<Note> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.Notes.Where(n => n.UserId == userId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
```

**`DependencyInjection.cs`** (Infrastructure) addition:

```csharp
services.AddScoped<INoteRepository, NoteRepository>();
```

## 6. EF Core migration

- **Name**: `AddNotes`
- **Command**:
  ```bash
  dotnet ef migrations add AddNotes \
    --project apps/api/src/NoteManagement.Infrastructure \
    --startup-project apps/api/src/NoteManagement.Api

  dotnet ef database update \
    --project apps/api/src/NoteManagement.Infrastructure \
    --startup-project apps/api/src/NoteManagement.Api
  ```
- **Entity changes**: adds the `Notes` table (`Id, UserId, Title, Content, CreatedAt, UpdatedAt, DeletedAt`), its `(UserId, DeletedAt, UpdatedAt)` index, and the `Notes.UserId → Users.Id` FK (cascade delete).
- **Backward compatible**: yes — purely additive on top of AB-1003's `AddPasswordResetOtps` migration; no existing table/column is touched. `HasQueryFilter` is query-time only and produces no schema change.

## 7. Api layer

**`ClaimsPrincipalExtensions`** (new — factors out the `sub`-claim extraction `AuthController.GetMe` already does inline, for `NotesController`'s five actions that all need it):

```csharp
// Api/Extensions/ClaimsPrincipalExtensions.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NoteManagement.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Same 'sub'-claim extraction AuthController.GetMe already performs inline — factored out here since every NotesController action needs it. Throws if called on a request that reached an [Authorize]-protected action without a 'sub' claim, which should never happen (see AuthController.GetMe's identical precedent).</summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var subClaim = user.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("Authenticated request is missing its 'sub' claim.");
        return Guid.Parse(subClaim);
    }
}
```

**`NotesController`** — class-level `[Authorize]` (unlike `AuthController`'s per-action mix, every action here requires auth). Route ids are plain `Guid` (no `{id:guid}` route constraint) so a malformed id fails `[ApiController]` model binding and returns `400`, rather than a constraint mismatch falling through to an uninformative `404`:

```csharp
// Api/Controllers/NotesController.cs
[ApiController]
[Route("api/notes")]
[Authorize]
public sealed class NotesController : ControllerBase
{
    private readonly INoteService _noteService;

    public NotesController(INoteService noteService)
    {
        _noteService = noteService;
    }

    /// <summary>FRS-NOTE-001.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(NoteResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NoteResponseDto>> Create(CreateNoteRequestDto request, CancellationToken cancellationToken)
    {
        var note = await _noteService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, note);
    }

    /// <summary>FRS-NOTE-002/006/007 — fixed default view only (see NoteService.ListAsync remarks).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NoteListResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NoteListResponseDto>> List(CancellationToken cancellationToken)
    {
        var result = await _noteService.ListAsync(User.GetUserId(), cancellationToken);
        return Ok(result);
    }

    /// <summary>FRS-NOTE-002.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(NoteResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NoteResponseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var note = await _noteService.GetByIdAsync(User.GetUserId(), id, cancellationToken);
        return Ok(note);
    }

    /// <summary>FRS-NOTE-003.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(NoteResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NoteResponseDto>> Update(Guid id, UpdateNoteRequestDto request, CancellationToken cancellationToken)
    {
        var note = await _noteService.UpdateAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(note);
    }

    /// <summary>FRS-NOTE-004.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _noteService.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }

    /// <summary>FRS-NOTE-005.</summary>
    [HttpPost("{id}/restore")]
    [ProducesResponseType(typeof(NoteResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NoteResponseDto>> Restore(Guid id, CancellationToken cancellationToken)
    {
        var note = await _noteService.RestoreAsync(User.GetUserId(), id, cancellationToken);
        return Ok(note);
    }
}
```

No try/catch — `ProblemDetailsExceptionHandler` gains two mappings, inserted into the existing `switch` expression alongside the AB-1002/AB-1003 ones:

```csharp
NoteNotFoundException => (StatusCodes.Status404NotFound, "Note not found"),
NoteNotDeletedException => (StatusCodes.Status409Conflict, "Note is not deleted"),
```

Anything unmapped still falls through to the generic 500, unchanged.

### 7a. Swagger UI — JWT Bearer Authorize button (user-requested fix)

`AddSwaggerGen()` in `Program.cs` has taken no arguments since AB-1001 — it has never registered a security scheme, so Swagger UI has never shown an Authorize button, and none of AB-1002/AB-1003's `[Authorize]` endpoints (nor this ticket's 5) have ever been callable through Swagger UI without hand-editing a request. Fixed by adding a `Bearer` security definition + a global security requirement:

```csharp
// Program.cs — replace builder.Services.AddSwaggerGen(); with:
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the access token returned by POST /api/auth/login or /api/auth/refresh (no \"Bearer \" prefix — Swashbuckle adds it).",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});
```

Requires `using Microsoft.OpenApi;` (new `using` in `Program.cs`). **Correction during implementation**: the package pinned in `Directory.Packages.props` (`Swashbuckle.AspNetCore 10.2.3`) resolves `Microsoft.OpenApi 2.7.5`, whose v10 public API moved every type out of the old `Microsoft.OpenApi.Models` namespace into `Microsoft.OpenApi` directly, and changed `AddSecurityRequirement` to take a `Func<OpenApiDocument, OpenApiSecurityRequirement>` — the reference to the just-defined scheme is built via `new OpenApiSecuritySchemeReference("Bearer", document)` rather than the old `OpenApiReference`/`ReferenceType.SecurityScheme` pair. Verified against Swashbuckle.AspNetCore's own v10 migration guide and `BearerAuthentication` sample (via Context7) after the pre-v10-style code above initially failed to compile. No `Directory.Packages.props` change either way — `Microsoft.OpenApi` ships transitively with `Swashbuckle.AspNetCore`, already referenced.

**Deliberately global, not per-endpoint**: `AddSecurityRequirement` here applies to every operation in the document, including `[AllowAnonymous]` ones (`register`/`login`/`refresh`/`logout`/`forgot-password`/`reset-password`) — Swagger UI will show a lock icon on those too, even though they don't require a token. This is a cosmetic imprecision only: authorization is still enforced entirely server-side by each action's `[Authorize]`/`[AllowAnonymous]` attribute (SDS §29/AGENTS.md §7), Swagger's displayed lock icon has no bearing on it, and an anonymous endpoint still works from Swagger UI whether or not the Authorize button has been used. A precise per-endpoint scheme (an `IOperationFilter` checking for `AllowAnonymousAttribute`) is more code for a dev-only tooling nicety and is not what was asked for — not built here.

Scoped to `Program.cs` only — no controller, DTO, or `[Authorize]` attribute changes; purely additive to the existing Swagger document generation.

## 8. Shared TS contracts (`packages/shared`)

`src/schemas/notes.ts` (new):

```typescript
import { z } from 'zod';

// Mirrors CreateNoteRequestDto/UpdateNoteRequestDto's TrimmedLength(1, 200)/TrimmedLength(1, ∞)
// validation — z.string().trim() normalizes before .min()/.max() check length, matching the
// backend's "after trimming" rule. UX convenience only; the backend remains authoritative.
export const createNoteRequestSchema = z.object({
  title: z.string().trim().min(1).max(200),
  content: z.string().trim().min(1),
});
export type CreateNoteRequest = z.infer<typeof createNoteRequestSchema>;

export const updateNoteRequestSchema = createNoteRequestSchema;
export type UpdateNoteRequest = z.infer<typeof updateNoteRequestSchema>;

export const noteResponseSchema = z.object({
  id: z.string().uuid(),
  title: z.string(),
  content: z.string(),
  createdAt: z.string(),
  updatedAt: z.string(),
});
export type NoteResponse = z.infer<typeof noteResponseSchema>;

// {items, page, pageSize, totalCount, totalPages} — the standard list envelope (AGENTS.md §6).
export const noteListResponseSchema = z.object({
  items: z.array(noteResponseSchema),
  page: z.number().int(),
  pageSize: z.number().int(),
  totalCount: z.number().int(),
  totalPages: z.number().int(),
});
export type NoteListResponse = z.infer<typeof noteListResponseSchema>;
```

`src/types/notes.ts` (new) re-exports the derived types, same pattern as `types/auth.ts`:

```typescript
export type {
  CreateNoteRequest,
  UpdateNoteRequest,
  NoteResponse,
  NoteListResponse,
} from '../schemas/notes';
```

`src/index.ts` adds the 4 new schema values to a new named export block from `./schemas/notes`, plus `export * from './types/notes';` — same shape as the existing `./schemas/auth`/`./types/auth` block. No new dependency — `zod` is already a `packages/shared` dependency.

## 9. Reuse of existing code / patterns

- **Layering + DI-per-layer registration**, **fail-fast config style**, **`AddProblemDetails()`/`UseExceptionHandler()`**, **`[ApiController]` automatic model validation**, **JWT bearer pipeline** — all reused as-is, unmodified.
- **`IUnitOfWork.RunInTransactionAsync`** — reused verbatim for every `NoteService` write, matching `AuthService`'s convention.
- **EF Core global query filter** (new pattern for this codebase, first entity to need one) — `HasQueryFilter(n => n.DeletedAt == null)` on `NoteConfiguration`, with `IgnoreQueryFilters()` used only by `GetByIdIncludingDeletedAsync` (restore's lookup). This single filter is what makes "soft-deleted note behaves like a 404" fall out of `GetByIdAsync` automatically, rather than needing an explicit `DeletedAt == null` check repeated in every query and in `NoteService`.
- **Hand-rolled test fakes** (`AuthServiceTests`'s convention: private nested fake classes + a `CreateSut` factory) — `NoteServiceTests` follows the same shape with `FakeNoteRepository`/`FakeUnitOfWork` (the latter can likely be copied near-verbatim from `AuthServiceTests`'s existing `FakeUnitOfWork`, which is already store-agnostic).
- **`WebApplicationFactory<Program>` + isolated LocalDB database per test class** (`AuthControllerTests`'s convention) — `NotesControllerTests` follows the same `ClassInitialize`/`ClassCleanup` shape with its own dedicated `NoteManagement...NotesControllerTests` LocalDB database, and registers/logs in a test user via the real `/api/auth/register` + `/api/auth/login` endpoints to obtain a bearer token for authenticated requests (no separate auth stub needed — AB-1002's endpoints are already live).

## 10. Checkpoint commands

Run in order; fix and re-run on first failure before moving to the next (`CLAUDE.md` Quality Gates).

**Backend** (after §3–§7 file changes):
```bash
dotnet ef migrations add AddNotes --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
```

**Frontend/shared** (after §8 file changes — `packages/shared` only, no `apps/web` changes this ticket):
```bash
pnpm install
pnpm lint --max-warnings 0
pnpm build
pnpm test
```

**Full-repo gate before this ticket is considered done:**
```bash
pnpm lint --max-warnings 0
pnpm build
pnpm test --coverage
dotnet build apps/api/NoteManagement.sln
dotnet test apps/api/NoteManagement.sln --collect:"XPlat Code Coverage"
```

## 11. Tests planned (spec scenario → test)

`/tasks` will enumerate these as individual checklist items; listed here to confirm every spec scenario has a planned home before implementation starts.

| Spec scenario | Test |
|---|---|
| Successful note creation | `NoteServiceTests.CreateAsync_WithValidData_CreatesNote` (Unit) + `NotesControllerTests.Create_WithValidData_Returns201WithNote` (Integration) |
| Missing title rejected | `NotesControllerTests.Create_WithMissingOrBlankTitle_Returns400` (Integration — DataAnnotations short-circuits before `NoteService`, same precedent as `Register_WithInvalidEmail_Returns400`) |
| Title exceeding maximum length rejected | `NotesControllerTests.Create_WithTitleOver200Chars_Returns400` (Integration) |
| Missing content rejected | `NotesControllerTests.Create_WithMissingOrBlankContent_Returns400` (Integration) |
| Unauthenticated request rejected | `NotesControllerTests.Create_WithoutAccessToken_Returns401` (Integration) |
| Owner retrieves their own note | `NoteServiceTests.GetByIdAsync_WithOwnedNote_ReturnsNote` (Unit) + `NotesControllerTests.GetById_WithOwnedNote_Returns200` (Integration) |
| Non-existent note rejected | `NoteServiceTests.GetByIdAsync_WithUnknownId_ThrowsNoteNotFoundException` (Unit) + `NotesControllerTests.GetById_WithUnknownId_Returns404` (Integration) |
| Another user's note rejected identically to not-found | `NotesControllerTests.GetById_WithAnotherUsersNote_Returns404` (Integration — second registered/logged-in user) |
| Soft-deleted note rejected identically to not-found | `NotesControllerTests.GetById_AfterSoftDelete_Returns404` (Integration) |
| Lists only the caller's active notes | `NoteServiceTests.ListAsync_ReturnsOnlyCallersActiveNotesSortedByUpdatedAtDesc` (Unit) + `NotesControllerTests.List_ReturnsOwnedNotesWithPaginationEnvelope` (Integration) |
| Soft-deleted notes excluded from the list | `NotesControllerTests.List_ExcludesSoftDeletedNotes` (Integration) |
| Another user's notes excluded from the list | `NotesControllerTests.List_ExcludesOtherUsersNotes` (Integration) |
| Empty list for a user with no notes | `NoteServiceTests.ListAsync_WithNoNotes_ReturnsEmptyEnvelope` (Unit) |
| Owner successfully updates their note | `NoteServiceTests.UpdateAsync_WithValidData_UpdatesTitleContentAndUpdatedAt` (Unit) + `NotesControllerTests.Update_WithValidData_Returns200WithUpdatedNote` (Integration) |
| Invalid update rejected | `NotesControllerTests.Update_WithInvalidData_Returns400AndDoesNotModifyNote` (Integration) |
| Update to non-existent note rejected | `NoteServiceTests.UpdateAsync_WithUnknownId_ThrowsNoteNotFoundException` (Unit) + `NotesControllerTests.Update_WithUnknownId_Returns404` (Integration) |
| Update to another user's note rejected identically to not-found | `NotesControllerTests.Update_WithAnotherUsersNote_Returns404` (Integration) |
| Update to a soft-deleted note rejected | `NotesControllerTests.Update_AfterSoftDelete_Returns404` (Integration) |
| Owner soft-deletes their note | `NoteServiceTests.DeleteAsync_WithOwnedNote_SetsDeletedAt` (Unit) + `NotesControllerTests.Delete_WithOwnedNote_Returns204` (Integration) |
| Soft-deleted note excluded from subsequent retrieval | `NotesControllerTests.Delete_ThenGetById_Returns404` (Integration) |
| Delete of non-existent note rejected | `NoteServiceTests.DeleteAsync_WithUnknownId_ThrowsNoteNotFoundException` (Unit) + `NotesControllerTests.Delete_WithUnknownId_Returns404` (Integration) |
| Delete of another user's note rejected identically to not-found | `NotesControllerTests.Delete_WithAnotherUsersNote_Returns404AndNoteRemainsActive` (Integration) |
| Delete of an already soft-deleted note rejected | `NotesControllerTests.Delete_CalledTwice_SecondCallReturns404` (Integration) |
| Owner restores their soft-deleted note | `NoteServiceTests.RestoreAsync_WithDeletedNote_ClearsDeletedAt` (Unit) + `NotesControllerTests.Restore_WithSoftDeletedNote_Returns200` (Integration) |
| Restored note reappears in retrieval and listing | `NotesControllerTests.Restore_ThenGetByIdAndList_NoteIsActive` (Integration) |
| Restore of non-existent note rejected | `NoteServiceTests.RestoreAsync_WithUnknownId_ThrowsNoteNotFoundException` (Unit) + `NotesControllerTests.Restore_WithUnknownId_Returns404` (Integration) |
| Restore of another user's note rejected identically to not-found | `NotesControllerTests.Restore_WithAnotherUsersNote_Returns404` (Integration) |
| Restore of a not-deleted note rejected as a conflict | `NoteServiceTests.RestoreAsync_WithActiveNote_ThrowsNoteNotDeletedException` (Unit) + `NotesControllerTests.Restore_WithActiveNote_Returns409` (Integration) |
| Restore succeeds regardless of elapsed time since deletion | `NoteServiceTests.RestoreAsync_LongAfterDeletion_StillSucceeds` (Unit — fake repo returns a note with an old DeletedAt; no purge logic exists to interfere) |
| `Note.IsDeleted`/`UpdateContent()`/`SoftDelete()`/`Restore()` domain rules | `NoteTests` (Unit, Domain — no infrastructure, matches `RefreshTokenTests`'/`PasswordResetOtpTests`' precedent): `IsDeleted_WhenNotDeleted_ReturnsFalse`, `IsDeleted_AfterSoftDelete_ReturnsTrue`, `UpdateContent_SetsNewValuesAndBumpsUpdatedAt_LeavesCreatedAtUnchanged`, `SoftDelete_WhenCalledTwice_KeepsFirstDeletedAtTimestamp`, `Restore_ClearsDeletedAt` |

Every named test follows `Method_Condition_ExpectedResult` (AGENTS.md §6/§10).

## 12. Explicitly not doing in this ticket

- Tags on notes / `NoteTags` (AB-1006).
- Client-driven pagination/sorting/tag-filtering query params on `GET /api/notes` (AB-1005) — `NoteRepository.GetPageForUserAsync`'s `page`/`pageSize` parameters exist now purely so that ticket doesn't need to change this signature.
- `NoteVersions` snapshot-on-save (AB-1009).
- Search (AB-1007), sharing (AB-1008).
- Any frontend notes UI consuming these endpoints (AB-1011/AB-1012) — `packages/shared` additions are the only cross-boundary artifact this ticket produces for those later tickets.
- Automatic permanent purge of soft-deleted notes after any retention window — no ticket currently owns this (proposal.md flags it as a gap, not silently assumed); `RestoreAsync` therefore has no time-based rejection.
- Any change to `Users`/`RefreshTokens`/`PasswordResetOtps`/`AuthController`/`AuthService` (AB-1002/AB-1003, already shipped and unmodified here) beyond nothing — this ticket adds a parallel capability, not an extension of `authentication`.
