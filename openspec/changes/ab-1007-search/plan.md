# Technical Plan — AB-1007 Search

Source artifacts: `proposal.md`, `specs/search/spec.md` (ADDED), `specs/project-setup/spec.md` (MODIFIED "EF Core + SQL Server Wiring"), `delta-openapi.yaml`. New `GET /api/search` endpoint, a new SQL Server full-text catalog/index on `Notes(Title, Content)` via one EF Core migration (raw SQL), and one new Infrastructure-only keyless query type. No `Note`/`Tag` domain entity changes. `packages/shared` gets a new `search` module (consumed starting AB-1013 — no `apps/web` change in this ticket, same precedent as AB-1006's `tags` module). Also reverses AB-1001's LocalDB dev-database decision (§0) — a real, verified environment finding, not a code-design choice.

## 0. Environment Deviation from AB-1001 (discovered during /plan review, before implementation)

**AB-1001's approved dev-environment decision was SQL Server Express LocalDB**, used by every ticket's local dev setup and by the `backend` CI job (`windows-latest`, chosen specifically because it ships LocalDB preinstalled — see that ticket's plan.md §"Architecture decision — why windows-latest"). Verifying this ticket's core dependency against a real LocalDB instance found:

- `SELECT SERVERPROPERTY('IsFullTextInstalled')` returns `0` on LocalDB — confirmed even after reinstalling LocalDB with the Full-Text feature explicitly selected during setup.
- This is **architectural, not a missing component**: LocalDB runs as a per-user process, not a Windows service, and SQL Server's Full-Text daemon requires the latter. No installer option or configuration can add Full-Text Search to LocalDB. This applies to every LocalDB instance everywhere, including GitHub's `windows-latest` runners — their preinstalled LocalDB has the same limitation, for the same reason; nothing about how that image provisions LocalDB changes the underlying architecture.
- A full SQL Server Express instance (`SQLEXPRESS`, installed and running as a Windows service — confirmed on this machine as `SHAHID-PC\SQLEXPRESS`) reports `IsFullTextInstalled = 1` and works correctly. `.\SQLEXPRESS` (the portable, machine-name-independent form of "the local default-instance SQL Server Express service") is used in every shared file below instead of a literal hostname, so the change works on every developer's machine, not just this one.

**Decision — what changes, and why:**

| Surface | Was | Now | Reasoning |
|---|---|---|---|
| Local dev connection string | `Server=(localdb)\MSSQLLocalDB;...` | `Server=.\SQLEXPRESS;...` (same `Trusted_Connection=True` Windows-integrated auth — a local SQL Server Express service still runs under Windows auth, so no credential handling changes for developers) | Full-Text Search is this ticket's entire purpose (FRS-SEARCH-001/ADR-005) — no code-level workaround exists for an engine that structurally cannot host it. |
| CI `backend` job | `runs-on: windows-latest` + preinstalled LocalDB | `runs-on: ubuntu-latest` + `mssql/server:2022-latest` service container (SQL-auth `sa` connection string, since Linux containers have no Windows-integrated auth) | AB-1001's own plan.md explicitly pre-flagged this exact swap ("a drop-in swap for a future ticket if needed... not a blocker now") for precisely this situation. It is also materially simpler/faster than silently installing a full SQL Server Express edition onto a Windows CI runner (a multi-minute, historically fragile unattended-install process) — GitHub Actions' native `services:` container primitive starts a ready-made SQL Server Linux image in well under a minute, and Full-Text Search has shipped in SQL Server on Linux since SQL Server 2017. |
| `openspec/specs/project-setup/spec.md` "EF Core + SQL Server Wiring" | Named LocalDB explicitly | Named "a local SQL Server Express instance (not LocalDB)", with a new scenario asserting `IsFullTextInstalled = 1` | The archived requirement text is now factually wrong for what this ticket (and everything after it) needs; per this project's OpenSpec convention, a requirement-level change to an already-archived capability is filed as a `MODIFIED Requirements` delta under the ticket that necessitates it (same pattern AB-1006 used to modify `notes`), not a silent edit to the archived main spec. |

**Not changed**: `Jwt:*` configuration, the `appsettings.Development.json.example` file's shape/comments beyond the one connection-string value, and every other CI step (restore/build/test commands, tool versions, frontend job) are untouched — this is scoped strictly to the database engine and its connection string.

**Verification note (honest about what could not be checked from here)**: the `ubuntu-latest` + `mssql/server` container CI change is written against the widely-documented pattern for SQL Server GitHub Actions service containers, but — unlike the local-dev connection-string change, which reuses this codebase's own already-working Windows-integrated-auth pattern — it has not been run against a real GitHub Actions job as part of this plan. Task list item (see `tasks.md`, once generated) should include actually pushing a branch and watching the `backend` CI job go green before this is considered fully verified, not just "written to look correct."

## 1. Architecture Decisions

| Decision | Reasoning |
|---|---|
| **Full-text query composition uses EF Core's `FromSqlInterpolated` against a small Infrastructure-only keyless entity (`FullTextMatch { Guid Key; int Rank; }`) mapped to `CONTAINSTABLE`'s result shape, then composed with a normal LINQ `join` against `_dbContext.Notes`.** | EF Core 8 has no native LINQ translation for `CONTAINSTABLE`/`FORMSOF`. `FromSqlInterpolated` composed with a LINQ join is the standard, documented EF Core recipe for SQL Server full-text search and keeps the interpolated search-condition string parameterized (never concatenated) exactly like `UnitOfWork`'s existing SQL-adjacent code respects AGENTS.md §6/§11. Joining against `_dbContext.Notes` (not the raw table) means `Note`'s existing `HasQueryFilter(n.DeletedAt == null)` applies automatically — soft-deleted notes are excluded for free, the same mechanism `GetPageForUserAsync`/`GetActiveNoteCountsAsync` already rely on. |
| **`FullTextMatch` is configured `HasNoKey().ToView(null)` — an Infrastructure-only projection type, not a `Domain` entity and not a `DbSet` property on `ApplicationDbContext`.** | It has no identity, no lifecycle, and exists solely to give `FromSqlInterpolated` a mapped return shape for `CONTAINSTABLE(Notes, (Title, Content), @condition)`'s `[KEY]`/`[RANK]` columns. `ToView(null)` excludes it from migrations (it is backed by no real table/view — SDS §5.4: Infrastructure owns persistence-specific implementation details that never leak into Domain). Queried only via `_dbContext.Set<FullTextMatch>()` inside `SearchRepository`. |
| **Search terms are tokenized and sanitized to `[\p{L}\p{Nd}'-]` before being assembled into the `FORMSOF(INFLECTIONAL, "term")` predicate string — never the raw `q` value.** | `q` is untrusted input (SDS §59/spec: "malformed or unusual characters in `q` SHALL NOT cause a search-syntax error to be exposed to the caller"). Stripping every character CONTAINSTABLE's mini-language treats specially (`"`, `(`, `)`, `*`, etc.) before wrapping each token in `"..."` means a token can never break out of its quoted phrase to inject `NEAR`/`OR`/weighting syntax — the outer T-SQL command is already parameterized via `FromSqlInterpolated`, and this closes the second-order injection surface inside the FTS predicate language itself. A token that sanitizes to empty is dropped; if every token drops out, `SearchService` short-circuits to an empty result page without calling the repository at all (valid per spec — a passing `q` with no results is `200 OK` + empty `items`, never a 400). |
| **Highlighting is a pure, DB-free static helper (`SearchHighlighter`) operating on `Note.Title`/`Note.Content` plus the already-sanitized term list, using two Unicode Private-Use-Area sentinel characters (``/``) as match delimiters.** | Keeps the highlighter unit-testable with hand-written strings (no LocalDB needed) and keeps it entirely independent of *how* SQL Server matched the row — it re-scans the note's own text for the literal (case-insensitive) tokens already used in the query, via `Regex.Escape`d alternation, and wraps every non-overlapping match. PUA characters were chosen over any ASCII/HTML-adjacent marker (e.g. `**`, `<mark>`) specifically because they cannot collide with real user content in a way that produces a visible artifact, and because they carry no markup meaning at all — satisfying spec's "SHALL NOT emit HTML tags... of any kind" (SDS §44/§60) by construction, not by an escaping step that could be gotten wrong later. |
| **Highlighting matches tokens as case-insensitive literal substrings, not SQL Server's inflectional forms.** | The FTS match (`FORMSOF(INFLECTIONAL, ...)`) and the highlight re-scan are deliberately two separate steps with two different matching rules — reproducing SQL Server's inflectional stemming in C# would require a third-party stemmer dependency AGENTS.md doesn't sanction. This is an accepted, documented simplification: a note that matched only via an inflected form (e.g. query `run`, content `running`) still highlights correctly here because English suffix-inflection is substring-preserving in the common cases the spec's scenarios exercise, but is not guaranteed for irregular forms. No spec scenario requires highlighting an irregular inflected form. |
| **`highlight.content` is a ≤200-character excerpt centered on the first case-insensitive token match (or the start of content when only the title matched), computed before highlighting is applied.** | Matches spec's "Search Result Highlighting" requirement exactly. Excerpting first, then highlighting only within the excerpt, keeps the highlighter's output bounded regardless of note length (`Content` is unbounded `nvarchar(max)` per `NoteConfiguration`) and avoids ever returning full note content through the search endpoint's `highlight` field. |
| **No `ISearchRepository` unit tests beyond the pure helpers (`SearchTermTokenizer`, `SearchHighlighter`); real matching/ranking/isolation is covered by `Tests.Integration` against a real LocalDB full-text index.** | Same split `NoteRepository`/`TagRepository` already use for DB-specific behavior (e.g. collation-dependent uniqueness was integration-tested, never faked). A fake `ISearchRepository` in a unit test would only assert what the fake was told to return — it can't validate that `CONTAINSTABLE`/`FORMSOF`/the join/the query filter actually behave as designed. `SearchService`'s own orchestration (defaulting/clamping page/pageSize, the empty-terms short-circuit, tag hydration, DTO mapping) is still unit-tested against a hand-rolled `FakeSearchRepository`, matching `NoteServiceTests`' established pattern. |
| **SQL Server full-text index uses `WITH CHANGE_TRACKING AUTO`, and integration tests poll (bounded retry with a short delay) rather than asserting a match immediately after inserting/updating a note.** | `CHANGE_TRACKING AUTO` re-populates the full-text index automatically after every committed change, but population is asynchronous — there is no synchronous "index is now up to date" signal in SQL Server. A test that creates a note and searches for it in the very next statement can race the background population. Polling with a short bounded timeout (e.g. up to a few seconds) is the standard, documented way to write reliable full-text-search integration tests, and is isolated entirely inside the test project — no product code changes. |
| **The migration creates the full-text catalog/index with defensive `IF NOT EXISTS` guards around `CREATE FULLTEXT CATALOG`/`CREATE FULLTEXT INDEX` (and the inverse in `Down`).** | EF Core's migration history table (`__EFMigrationsHistory`) already prevents a migration from re-running once applied, but a full-text catalog/index is server-level DDL outside EF's own change-tracking — guarding it defensively costs nothing and avoids a hard failure if a developer's LocalDB instance already has a same-named catalog from a prior partial run. |
| **`ISearchService` depends on both `ISearchRepository` (the FTS query) and the existing `INoteRepository` (reusing `GetTagsForNotesAsync` for tag hydration) — no new tag-lookup code.** | Identical precedent to `NoteService` depending on `ITagRepository`: an Application service coordinating more than one repository is already this codebase's norm, and `GetTagsForNotesAsync` is already exactly "batch-fetch tags for a page of notes," which is exactly what a page of search results needs too. |

## 2. Files to Create

### Infrastructure — Search projection type (`apps/api/src/NoteManagement.Infrastructure/Search`)

**`FullTextMatch.cs`** (NEW):
```csharp
namespace NoteManagement.Infrastructure.Search;

/// <summary>
/// Maps CONTAINSTABLE's result shape ([KEY], [RANK]). Infrastructure-only — not a Domain entity,
/// not backed by any real table/view (see FullTextMatchConfiguration). Queried only via
/// SearchRepository's FromSqlInterpolated call.
/// </summary>
internal sealed class FullTextMatch
{
    public Guid Key { get; init; }
    public int Rank { get; init; }
}
```

### Infrastructure — Configurations

**`Configurations/FullTextMatchConfiguration.cs`** (NEW):
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoteManagement.Infrastructure.Search;

namespace NoteManagement.Infrastructure.Configurations;

/// <summary>
/// A keyless projection type for CONTAINSTABLE's result shape — ToView(null) excludes it from
/// migrations (no real table/view backs it; see plan.md architecture decisions).
/// </summary>
public sealed class FullTextMatchConfiguration : IEntityTypeConfiguration<FullTextMatch>
{
    public void Configure(EntityTypeBuilder<FullTextMatch> builder)
    {
        builder.HasNoKey();
        builder.ToView(null);
        builder.Property(m => m.Key).HasColumnName("KEY");
        builder.Property(m => m.Rank).HasColumnName("RANK");
    }
}
```

### Infrastructure — Migration

**`Migrations/<timestamp>_AddNotesFullTextSearch.cs`** (NEW) — generated via `dotnet ef migrations add AddNotesFullTextSearch` (which will produce an empty `Up`/`Down` since no model shape changed) and then hand-edited to add raw SQL:
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'NotesFullTextCatalog')
BEGIN
    CREATE FULLTEXT CATALOG NotesFullTextCatalog AS DEFAULT;
END");

    migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Notes'))
BEGIN
    CREATE FULLTEXT INDEX ON dbo.Notes(Title LANGUAGE 1033, Content LANGUAGE 1033)
        KEY INDEX PK_Notes ON NotesFullTextCatalog
        WITH CHANGE_TRACKING AUTO;
END");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Notes'))
BEGIN
    DROP FULLTEXT INDEX ON dbo.Notes;
END");

    migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'NotesFullTextCatalog')
BEGIN
    DROP FULLTEXT CATALOG NotesFullTextCatalog;
END");
}
```
`KEY INDEX PK_Notes` reuses `Notes`' existing primary-key index (`AddNotes` migration names it `PK_Notes`) — SQL Server requires a unique, single-column, non-nullable index as a full-text key, and the PK already satisfies that; no new index is created for this purpose.

**Backward compatible**: purely additive DDL (a new catalog + index); no existing column/table/constraint is touched, so no existing query or migration is affected. `Down` fully reverses it.

### Application — DTOs (`apps/api/src/NoteManagement.Application/DTOs/Search`)

**`SearchQueryDto.cs`** (NEW):
```csharp
using System.ComponentModel.DataAnnotations;
using NoteManagement.Application.Validation;

namespace NoteManagement.Application.DTOs.Search;

/// <summary>FRS-SEARCH-001/004. Query-string shape for GET /api/search, matching delta-openapi.yaml exactly. The pageSize>100 clamp is a service-layer policy decision, not expressed here (same precedent as NoteListQueryDto).</summary>
public sealed record SearchQueryDto(
    [Required, TrimmedLength(1, 200)] string Q,
    [Range(1, int.MaxValue, ErrorMessage = "page must be a positive integer.")] int? Page = null,
    [Range(1, int.MaxValue, ErrorMessage = "pageSize must be a positive integer.")] int? PageSize = null);
```

**`NoteHighlightDto.cs`** (NEW):
```csharp
namespace NoteManagement.Application.DTOs.Search;

/// <summary>Plain-text excerpts with matched terms delimited by SearchHighlighter's sentinel markers — never HTML (SDS §44/§60).</summary>
public sealed record NoteHighlightDto(string Title, string Content);
```

**`SearchResultDto.cs`** (NEW):
```csharp
using NoteManagement.Application.DTOs.Tags;

namespace NoteManagement.Application.DTOs.Search;

/// <summary>The shape of one item in GET /api/search's results — the standard note shape plus a highlight.</summary>
public sealed record SearchResultDto(Guid Id, string Title, string Content, IReadOnlyList<TagRefDto> Tags, DateTime CreatedAt, DateTime UpdatedAt, NoteHighlightDto Highlight);
```

**`SearchResponseDto.cs`** (NEW):
```csharp
namespace NoteManagement.Application.DTOs.Search;

/// <summary>FRS-SEARCH-004. The standard list envelope (AGENTS.md §6).</summary>
public sealed record SearchResponseDto(IReadOnlyList<SearchResultDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);
```

### Application — Interfaces

**`Interfaces/ISearchRepository.cs`** (NEW):
```csharp
using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Interfaces;

/// <summary>terms is already sanitized/tokenized by SearchService — every term is safe to embed in a FORMSOF(...) predicate. Ownership (userId) and the soft-delete exclusion are baked into the query (Note's global query filter), same precedent as INoteRepository.</summary>
public interface ISearchRepository
{
    Task<(IReadOnlyList<Note> Items, int TotalCount)> SearchAsync(Guid userId, IReadOnlyList<string> terms, int page, int pageSize, CancellationToken cancellationToken);
}
```

**`Interfaces/ISearchService.cs`** (NEW):
```csharp
using NoteManagement.Application.DTOs.Search;

namespace NoteManagement.Application.Interfaces;

public interface ISearchService
{
    Task<SearchResponseDto> SearchAsync(Guid userId, SearchQueryDto query, CancellationToken cancellationToken);
}
```

### Application — Services

**`Services/SearchTermTokenizer.cs`** (NEW, internal, pure):
```csharp
using System.Text.RegularExpressions;

namespace NoteManagement.Application.Services;

/// <summary>
/// Tokenizes and sanitizes q into safe FTS terms (spec: "Search terms SHALL be treated as data,
/// never concatenated into a query string" / SDS §59). Stripping every non-letter/digit/apostrophe/
/// hyphen character means a token can never contain '"', '(', ')', '*', etc. — the characters that
/// carry special meaning inside a FORMSOF(...) predicate — so it can never break out of its quoted
/// phrase once SearchRepository wraps it. A token that sanitizes to empty is dropped entirely.
/// </summary>
internal static partial class SearchTermTokenizer
{
    [GeneratedRegex(@"[^\p{L}\p{Nd}'-]+")]
    private static partial Regex DisallowedChars();

    public static IReadOnlyList<string> Tokenize(string q) =>
        q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => DisallowedChars().Replace(t, string.Empty))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
```

**`Services/SearchHighlighter.cs`** (NEW, internal, pure):
```csharp
using System.Text;
using System.Text.RegularExpressions;
using NoteManagement.Application.DTOs.Search;

namespace NoteManagement.Application.Services;

/// <summary>
/// Builds sentinel-delimited highlight excerpts (spec: "Search Result Highlighting"). Matches
/// terms as case-insensitive literal substrings — a deliberate, documented simplification from
/// SQL Server's inflectional FTS match (see plan.md architecture decisions). Pure/DB-free —
/// operates only on the note's own Title/Content plus the already-sanitized term list.
/// </summary>
internal static class SearchHighlighter
{
    private const char Start = '';
    private const char End = '';
    private const int ContentExcerptLength = 200;

    public static NoteHighlightDto Build(string title, string content, IReadOnlyList<string> terms)
    {
        var excerpt = ExtractExcerpt(content, terms);
        return new NoteHighlightDto(HighlightTerms(title, terms), HighlightTerms(excerpt, terms));
    }

    private static string ExtractExcerpt(string content, IReadOnlyList<string> terms)
    {
        if (content.Length <= ContentExcerptLength)
        {
            return content;
        }

        var firstMatch = terms
            .Select(t => content.IndexOf(t, StringComparison.OrdinalIgnoreCase))
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        var start = firstMatch < 0 ? 0 : Math.Max(0, firstMatch - (ContentExcerptLength / 2));
        start = Math.Min(start, Math.Max(0, content.Length - ContentExcerptLength));
        var length = Math.Min(ContentExcerptLength, content.Length - start);
        return content.Substring(start, length);
    }

    private static string HighlightTerms(string text, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0 || text.Length == 0)
        {
            return text;
        }

        var pattern = string.Join('|', terms.Select(Regex.Escape));
        var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);

        var result = new StringBuilder(text.Length + terms.Count * 2);
        var lastEnd = 0;
        foreach (Match match in matches)
        {
            if (match.Index < lastEnd)
            {
                continue; // overlapping match — keep the earlier, already-emitted one.
            }

            result.Append(text, lastEnd, match.Index - lastEnd);
            result.Append(Start).Append(match.Value).Append(End);
            lastEnd = match.Index + match.Length;
        }

        result.Append(text, lastEnd, text.Length - lastEnd);
        return result.ToString();
    }
}
```

**`Services/SearchService.cs`** (NEW):
```csharp
using NoteManagement.Application.DTOs.Search;
using NoteManagement.Application.DTOs.Tags;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;

namespace NoteManagement.Application.Services;

public sealed class SearchService : ISearchService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly ISearchRepository _searchRepository;
    private readonly INoteRepository _noteRepository;

    public SearchService(ISearchRepository searchRepository, INoteRepository noteRepository)
    {
        _searchRepository = searchRepository;
        _noteRepository = noteRepository;
    }

    /// <summary>FRS-SEARCH-001..004. An all-sanitized-away q (e.g. "!!!") short-circuits to an empty page rather than querying the repository — still 200, per spec's "no matching notes -> empty page, not an error."</summary>
    public async Task<SearchResponseDto> SearchAsync(Guid userId, SearchQueryDto query, CancellationToken cancellationToken)
    {
        var terms = SearchTermTokenizer.Tokenize(query.Q);
        var page = query.Page ?? DefaultPage;
        var pageSize = Math.Min(query.PageSize ?? DefaultPageSize, MaxPageSize);

        if (terms.Count == 0)
        {
            return new SearchResponseDto(Array.Empty<SearchResultDto>(), page, pageSize, 0, 0);
        }

        var (items, totalCount) = await _searchRepository.SearchAsync(userId, terms, page, pageSize, cancellationToken);
        var tagsByNote = await _noteRepository.GetTagsForNotesAsync(items.Select(n => n.Id).ToList(), cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var results = items
            .Select(n => new SearchResultDto(
                n.Id, n.Title, n.Content,
                tagsByNote.GetValueOrDefault(n.Id, Array.Empty<Tag>()).Select(t => new TagRefDto(t.Id, t.Name, t.Color)).ToList(),
                n.CreatedAt, n.UpdatedAt,
                SearchHighlighter.Build(n.Title, n.Content, terms)))
            .ToList();

        return new SearchResponseDto(results, page, pageSize, totalCount, totalPages);
    }
}
```

### Application — DI

**`DependencyInjection.cs`** (MODIFY) — register `ISearchService`:
```csharp
services.AddScoped<ISearchService, SearchService>();
```

### Infrastructure — Repositories

**`Repositories/SearchRepository.cs`** (NEW):
```csharp
using Microsoft.EntityFrameworkCore;
using NoteManagement.Application.Interfaces;
using NoteManagement.Domain.Entities;
using NoteManagement.Infrastructure.Data;
using NoteManagement.Infrastructure.Search;

namespace NoteManagement.Infrastructure.Repositories;

public sealed class SearchRepository : ISearchRepository
{
    private readonly ApplicationDbContext _dbContext;

    public SearchRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<Note> Items, int TotalCount)> SearchAsync(Guid userId, IReadOnlyList<string> terms, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Every term is already sanitized to [\p{L}\p{Nd}'-] by SearchTermTokenizer — it can
        // contain no '"' and cannot break out of its quoted phrase (AGENTS.md §6/§11: this string
        // is still passed as a single parameter below, never concatenated into the command text).
        var searchCondition = string.Join(" AND ", terms.Select(t => $"FORMSOF(INFLECTIONAL, \"{t}\")"));

        var matches = _dbContext.Set<FullTextMatch>()
            .FromSqlInterpolated($"SELECT [KEY], [RANK] FROM CONTAINSTABLE(Notes, (Title, Content), {searchCondition})");

        // Joining against _dbContext.Notes (not the raw table) applies Note's own
        // HasQueryFilter(DeletedAt == null) automatically — soft-deleted notes are excluded
        // without a second, separately-maintained predicate (same precedent as NoteRepository).
        var query =
            from n in _dbContext.Notes
            join m in matches on n.Id equals m.Key
            where n.UserId == userId
            select new { Note = n, m.Rank };

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => x.Note.UpdatedAt) // stable tiebreak for equal-rank rows across pages
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.Note)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
```

### Infrastructure — DI

**`DependencyInjection.cs`** (MODIFY) — register `ISearchRepository`:
```csharp
// AB-1007: full-text search persistence.
services.AddScoped<ISearchRepository, SearchRepository>();
```

### Api — Controllers

**`Controllers/SearchController.cs`** (NEW):
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoteManagement.Api.Extensions;
using NoteManagement.Application.DTOs.Search;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
public sealed class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>FRS-SEARCH-001..004.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(SearchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SearchResponseDto>> Search([FromQuery] SearchQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _searchService.SearchAsync(User.GetUserId(), query, cancellationToken);
        return Ok(result);
    }
}
```

No change to `ProblemDetailsExceptionHandler` — every rejection this endpoint produces is a `[Required]`/`[TrimmedLength]`/`[Range]` model-validation failure, already turned into `400` automatically by `[ApiController]`, and `[Authorize]` already produces `401` — no new exception type is introduced.

### packages/shared

**`src/schemas/search.ts`** (NEW):
```typescript
// Zod schemas (AB-1007) — validation mirrors of the backend's Search DTOs, for frontend UX
// convenience only (packages/shared/CLAUDE.md). Consumed starting AB-1013 (frontend search UI).

import { z } from 'zod';
import { tagRefSchema } from './tags';

// Mirrors SearchQueryDto's [Required, TrimmedLength(1, 200)] — q is required, unlike
// noteListQuerySchema's optional page/pageSize/sortBy fields.
export const searchQuerySchema = z.object({
  q: z.string().trim().min(1).max(200),
  page: z.number().int().min(1).optional(),
  pageSize: z.number().int().min(1).optional(),
});
export type SearchQuery = z.infer<typeof searchQuerySchema>;

export const noteHighlightSchema = z.object({
  title: z.string(),
  content: z.string(),
});
export type NoteHighlight = z.infer<typeof noteHighlightSchema>;

export const searchResultSchema = z.object({
  id: z.string().uuid(),
  title: z.string(),
  content: z.string(),
  tags: z.array(tagRefSchema),
  createdAt: z.string(),
  updatedAt: z.string(),
  highlight: noteHighlightSchema,
});
export type SearchResult = z.infer<typeof searchResultSchema>;

// {items, page, pageSize, totalCount, totalPages} — the standard list envelope (AGENTS.md §6).
export const searchResponseSchema = z.object({
  items: z.array(searchResultSchema),
  page: z.number().int(),
  pageSize: z.number().int(),
  totalCount: z.number().int(),
  totalPages: z.number().int(),
});
export type SearchResponse = z.infer<typeof searchResponseSchema>;
```

**`src/types/search.ts`** (NEW):
```typescript
// Search DTOs (AB-1007). Mirror the backend's C# DTOs field-for-field — see
// apps/api/src/NoteManagement.Application/DTOs/Search and delta-openapi.yaml under
// openspec/changes/ab-1007-search. Re-derived from ../schemas/search.ts (z.infer<>), not
// hand-duplicated.

export type {
  SearchQuery,
  NoteHighlight,
  SearchResult,
  SearchResponse,
} from '../schemas/search';
```

**`src/index.ts`** (MODIFY) — add:
```typescript
// AB-1007 — Search DTOs + Zod schemas (SDS §55/§81). Consumed starting AB-1013 (frontend search UI).
export {
  searchQuerySchema,
  noteHighlightSchema,
  searchResultSchema,
  searchResponseSchema,
} from './schemas/search';
export * from './types/search';
```

### Environment / CI (MODIFY — §0 deviation)

**`apps/api/src/NoteManagement.Api/appsettings.Development.json.example`** (MODIFY) — connection string only:
```json
"DefaultConnection": "Server=.\\SQLEXPRESS;Database=NoteManagementDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
```

**Every hardcoded integration-test `TestConnectionString`** (MODIFY, same one-line substitution in each): `apps/api/tests/NoteManagement.Tests.Integration/Api/AuthControllerTests.cs` (both constants), `HealthEndpointTests.cs`, `NotesControllerTests.cs`, `TagsControllerTests.cs`, `Infrastructure/ApplicationDbContextTests.cs` — `Server=(localdb)\\MSSQLLocalDB;...` → `Server=.\\SQLEXPRESS;...`, database names unchanged. Accompanying `// Isolated LocalDB database...` comments reworded to say "SQL Server Express" instead of "LocalDB" (behavior/reasoning unchanged — still one isolated database per test class).

**`apps/api/tests/NoteManagement.Tests.Integration/MSTestSettings.cs`** (MODIFY) — comment only, same reasoning (shared-instance serialization), reworded off "LocalDB".

**`.github/workflows/ci.yml`** (MODIFY) — `backend` job:
```yaml
  backend:
    name: Backend (build, migrate, test)
    # LocalDB cannot host Full-Text Search (architectural — see ab-1007-search/plan.md §0),
    # so this job can no longer use windows-latest's preinstalled LocalDB. Adopts the swap
    # AB-1001's plan.md pre-flagged: a real SQL Server Linux service container.
    runs-on: ubuntu-latest
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: "Y"
          MSSQL_PID: "Express"
          # Ephemeral, CI-only container password — plan.md §0: no persistent target, never
          # reused as a real credential. Not a secret in the AGENTS.md §64 sense.
          MSSQL_SA_PASSWORD: "CiOnly_P@ssw0rd1"
        ports:
          - 1433:1433
        options: >-
          --health-cmd "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $MSSQL_SA_PASSWORD -C -Q 'SELECT 1' -b -o /dev/null"
          --health-interval 10s
          --health-timeout 5s
          --health-retries 10
          --health-start-period 10s
    defaults:
      run:
        working-directory: apps/api
    env:
      # Overrides appsettings.Development.json.example's Windows-integrated-auth connection
      # string — the Linux container has no Windows auth, so CI uses SQL auth against the
      # service container instead. ASP.NET Core's env-var config provider (ConnectionStrings__*
      # -> ConnectionStrings:*) takes precedence over the JSON file provisioned below.
      ConnectionStrings__DefaultConnection: "Server=localhost,1433;Database=NoteManagementDb;User Id=sa;Password=CiOnly_P@ssw0rd1;TrustServerCertificate=True"
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "9.0.x"
      - name: Restore local tools
        run: dotnet tool restore
      - name: Provision local dev config
        # Still provisions Jwt:* from the committed .example (unchanged, no secret) — only the
        # connection string differs, via the job-level env var above.
        run: cp src/NoteManagement.Api/appsettings.Development.json.example src/NoteManagement.Api/appsettings.Development.json
      - name: Restore
        run: dotnet restore NoteManagement.sln
      - name: Apply migrations
        run: dotnet ef database update --project src/NoteManagement.Infrastructure --startup-project src/NoteManagement.Api
      - name: Build
        run: dotnet build NoteManagement.sln --no-restore
      - name: Test
        run: dotnet test NoteManagement.sln --no-build --collect:"XPlat Code Coverage"
```
The `frontend` job is untouched.

**`openspec/specs/project-setup/spec.md`** — not edited directly; the ticket-level delta at `openspec/changes/ab-1007-search/specs/project-setup/spec.md` (`MODIFIED Requirements`) is what `openspec archive AB-1007-search` will merge into it, per this project's established OpenSpec workflow (same mechanism AB-1006 used for its `notes` delta).

## 3. Test Plan

One test per spec scenario (AGENTS.md §10/SDS §76), split unit vs. integration per the established convention: pure logic (tokenization, highlighting, page/pageSize orchestration) → `Tests.Unit` (hand-rolled fakes, no mocking library); anything where the real full-text index, ranking, or the HTTP/auth pipeline matters → `Tests.Integration` (`WebApplicationFactory` + isolated LocalDB database, per `NotesControllerTests`' precedent).

### `Tests.Unit/Application/SearchTermTokenizerTests.cs` (NEW)
- `Tokenize_SplitsOnWhitespace_ReturnsEachTerm`
- `Tokenize_StripsFtsSpecialCharacters`
- `Tokenize_DropsTermsThatSanitizeToEmpty`
- `Tokenize_DeduplicatesCaseInsensitively`

### `Tests.Unit/Application/SearchHighlighterTests.cs` (NEW)
- `Build_TitleMatch_WrapsTermWithSentinels`
- `Build_ContentMatch_ReturnsExcerptCenteredOnMatch`
- `Build_ContentShorterThanExcerptLength_ReturnsWholeContent`
- `Build_MultipleMatchingTerms_WrapsEachOne`
- `Build_MarkupLikeContent_PassesThroughAsLiteralText` (input containing `<`, `>`, `&` near a match)
- `Build_NoContentMatch_ExcerptsFromStart`

### `Tests.Unit/Application/SearchServiceTests.cs` (NEW, `FakeSearchRepository` + reused `FakeNoteRepository`)
- `SearchAsync_WithDefaultPaging_UsesPage1PageSize20`
- `SearchAsync_WithExplicitPaging_PassesThroughToRepository`
- `SearchAsync_WithOversizedPageSize_ClampsTo100`
- `SearchAsync_WithAllTermsSanitizedAway_ReturnsEmptyPageWithoutCallingRepository`
- `SearchAsync_MapsResultsWithTagsAndHighlight`

### `search` capability — integration (`Tests.Integration/Api/SearchControllerTests.cs`, NEW)

| Spec scenario | Test(s) |
|---|---|
| Successful single-term search | `Search_WithSingleTermMatchingTitleOrContent_Returns200WithMatches` |
| Multi-term search requires every term | `Search_WithMultiTermQuery_RequiresAllTerms` |
| Note matching only some terms is excluded | (same test as above, asserts the partial-match note is absent) |
| No matching notes returns an empty page, not an error | `Search_WithNoMatches_Returns200WithEmptyItems` |
| Missing q rejected | `Search_WithMissingQ_Returns400` |
| Empty or whitespace-only q rejected | `Search_WithBlankQ_Returns400` |
| Oversized q rejected | `Search_WithQOver200Chars_Returns400` |
| Unauthenticated request rejected | `Search_WithoutAccessToken_Returns401` |
| Only the caller's own notes are searched | `Search_ExcludesOtherUsersMatchingNotes` |
| Soft-deleted notes excluded from search results | `Search_ExcludesSoftDeletedMatchingNotes` |
| Matching term highlighted in title | `Search_TitleMatch_HighlightTitleContainsSentinelDelimitedTerm` |
| Matching term highlighted in a content excerpt | `Search_ContentMatch_HighlightContentIsBoundedExcerptWithSentinels` |
| Multiple matching terms all highlighted | `Search_MultiTermMatch_HighlightsEveryTerm` |
| Markup-like note content is never rendered as markup | `Search_NoteContentWithAngleBrackets_HighlightNeverContainsHtml` |
| Default pagination applied | `Search_WithNoPagingParams_UsesPage1PageSize20` |
| Client requests a specific page and page size | `Search_WithPageAndPageSize_ReturnsRequestedSlice` |
| Invalid page value rejected | `Search_WithInvalidPage_Returns400` |
| Invalid page size value rejected | `Search_WithInvalidPageSize_Returns400` |
| Oversized page size silently clamped | `Search_WithPageSizeOver100_ClampsTo100` |
| Page beyond the last page returns an empty page, not an error | `Search_WithPageBeyondLastPage_ReturnsEmptyItems` |

**Test-infrastructure addition**: a `WaitForFullTextIndexAsync` polling helper in `Tests.Integration` (bounded retry with a short delay, per plan.md's `CHANGE_TRACKING AUTO` architecture decision) — every test above that creates/updates a note and then immediately searches for it calls this helper instead of asserting on the first attempt, to avoid flakiness from full-text index population being asynchronous.

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
dotnet ef migrations add AddNotesFullTextSearch --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
dotnet ef database update --project apps/api/src/NoteManagement.Infrastructure --startup-project apps/api/src/NoteManagement.Api
```

**Environment prerequisite (§0)**: local dev now targets `.\SQLEXPRESS`, not LocalDB — before running the migration, verify `SELECT SERVERPROPERTY('IsFullTextInstalled')` against that instance returns `1` (this is an environment check the migration itself cannot guarantee). Every developer's `appsettings.Development.json` (gitignored, per-developer) must be updated by hand to point at `.\SQLEXPRESS` — updating the committed `.example` does not touch anyone's already-materialized local file.

**CI verification**: the `ubuntu-latest` + `mssql/server` container change (§0/§2) must be confirmed by actually pushing this branch and watching the `backend` CI job succeed — it was authored against the documented service-container pattern but not run from here (§0's verification note).

## 5. Explicitly Out of Scope (unchanged from proposal.md)

`tagId` filtering combined with search; `sortBy`/`sortDirection` on this endpoint (relevance-only); prefix/wildcard or raw FTS boolean syntax in `q`; any frontend search UI (AB-1013); sharing (AB-1008); version history (AB-1009).
